using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.MultiAccount;
using Skinora.Users.Domain.Entities;

namespace Skinora.Fraud.Application.MultiAccount;

/// <summary>
/// Default <see cref="IMultiAccountDetector"/> implementation (T56 — 02 §14.3,
/// 03 §7.4). Cross-checks the supplied user against every other active account
/// for the documented signal set:
/// <list type="bullet">
///   <item><b>Strong:</b> identical <c>DefaultPayoutAddress</c> /
///         <c>DefaultRefundAddress</c> on another user → flag.</item>
///   <item><b>Supporting:</b> identical IP / device fingerprint
///         (<see cref="UserLoginLog"/>) → evidence only.</item>
///   <item><b>Supporting:</b> identical payment <c>FromAddress</c>
///         (<see cref="BlockchainTransaction"/>) excluding the admin-curated
///         exchange / custodial address list → evidence only.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Routes the strong-signal write through <see cref="IFraudFlagService"/> so
/// the audit log + outbox + (optional) emergency-hold cascade pipeline owned
/// by T54 stays the single seam for every fraud flag emission. The detector
/// itself owns the surrounding <c>SaveChanges</c>.
/// </para>
/// <para>
/// <b>Idempotency:</b> a non-rejected, non-soft-deleted <c>ACCOUNT_LEVEL</c>
/// <c>MULTI_ACCOUNT</c> flag short-circuits evaluation. Repeated triggers
/// (e.g. the user updates the wallet again before admin reviews) do not
/// produce duplicates.
/// </para>
/// </remarks>
public sealed class MultiAccountDetector : IMultiAccountDetector
{
    /// <summary>
    /// SystemSetting key for the admin-curated exchange / custodial address
    /// CSV. Mirrors the <c>auth.banned_countries</c> "NONE = empty list"
    /// marker pattern from T30 so admins can disable the rule without
    /// deleting the row.
    /// </summary>
    public const string ExchangeAddressesSettingKey = "multi_account.exchange_addresses";

    /// <summary>Sentinel value indicating the address set is empty (no exclusions).</summary>
    public const string ExchangeAddressesNoneMarker = "NONE";

    /// <summary>
    /// Limit on how many recent login rows feed the IP / device-fingerprint
    /// supporting signal. Keeps the query plan bounded for users with very
    /// active sessions; the strong signal is the deciding factor either way.
    /// </summary>
    public const int RecentLoginSampleSize = 25;

    private readonly AppDbContext _db;
    private readonly IFraudFlagService _flagService;

    public MultiAccountDetector(AppDbContext db, IFraudFlagService flagService)
    {
        _db = db;
        _flagService = flagService;
    }

    public async Task<MultiAccountEvaluationResult> EvaluateAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        // ---------- Idempotency ----------
        var alreadyFlagged = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .AnyAsync(
                f => f.UserId == userId
                     && f.Scope == FraudFlagScope.ACCOUNT_LEVEL
                     && f.Type == FraudFlagType.MULTI_ACCOUNT
                     && f.Status != ReviewStatus.REJECTED
                     && !f.IsDeleted,
                cancellationToken);
        if (alreadyFlagged)
            return MultiAccountEvaluationResult.AlreadyFlagged();

        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
            return MultiAccountEvaluationResult.NoSignal();

        // ---------- Strong signal — wallet address match ----------
        // Payout has priority over refund: if both addresses match different
        // accounts the payout collision is the more actionable evidence
        // (it's where funds settle). Either match is sufficient to flag.
        var payoutMatches = await FindWalletMatchesAsync(
            userId, user.DefaultPayoutAddress, refund: false, cancellationToken);
        var refundMatches = await FindWalletMatchesAsync(
            userId, user.DefaultRefundAddress, refund: true, cancellationToken);

        MultiAccountMatchType? primaryType = null;
        string? primaryValue = null;
        IReadOnlyList<LinkedAccountDto> primaryLinked = [];

        primaryType = MultiAccountSignalEvaluator.PickStrongMatchType(
            hasPayoutMatch: payoutMatches.Count > 0,
            hasRefundMatch: refundMatches.Count > 0);

        if (primaryType is null)
            return MultiAccountEvaluationResult.NoSignal();

        if (primaryType == MultiAccountMatchType.WALLET_PAYOUT)
        {
            primaryValue = user.DefaultPayoutAddress;
            primaryLinked = payoutMatches;
        }
        else
        {
            primaryValue = user.DefaultRefundAddress;
            primaryLinked = refundMatches;
        }

        // ---------- Supporting signals (evidence only — never flag alone) ----------
        var supporting = await CollectSupportingSignalsAsync(userId, cancellationToken);

        // ---------- Stage flag through the T54 pipeline ----------
        var details = JsonSerializer.Serialize(new
        {
            matchType = primaryType.ToString(),
            matchValue = primaryValue,
            linkedAccounts = primaryLinked.Select(a => new
            {
                steamId = a.SteamId,
                displayName = a.DisplayName,
            }).ToArray(),
            supportingSignals = supporting.Select(s => new
            {
                type = s.Type,
                value = s.Value,
                linkedAccounts = s.LinkedAccounts.Select(a => new
                {
                    steamId = a.SteamId,
                    displayName = a.DisplayName,
                }).ToArray(),
            }).ToArray(),
        });

        var flagId = await _flagService.StageAccountFlagAsync(
            userId: userId,
            type: FraudFlagType.MULTI_ACCOUNT,
            details: details,
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cascadeEmergencyHold: false,
            emergencyHoldReason: null,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return MultiAccountEvaluationResult.Flagged(
            primaryType.Value,
            primaryValue!,
            primaryLinked.Count,
            supporting.Count,
            flagId);
    }

    private async Task<List<LinkedAccountDto>> FindWalletMatchesAsync(
        Guid userId, string? address, bool refund, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
            return [];

        var query = _db.Set<User>().AsNoTracking()
            .Where(u => u.Id != userId && !u.IsDeleted && !u.IsDeactivated);

        query = refund
            ? query.Where(u => u.DefaultRefundAddress == address)
            : query.Where(u => u.DefaultPayoutAddress == address);

        return await query
            .Select(u => new LinkedAccountDto(u.SteamId, u.SteamDisplayName))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<SupportingSignalDto>> CollectSupportingSignalsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var signals = new List<SupportingSignalDto>();

        // ---------- Recent login IPs / device fingerprints ----------
        var recentLogins = await _db.Set<UserLoginLog>()
            .AsNoTracking()
            .Where(l => l.UserId == userId && !l.IsDeleted)
            .OrderByDescending(l => l.CreatedAt)
            .Take(RecentLoginSampleSize)
            .Select(l => new { l.IpAddress, l.DeviceFingerprint })
            .ToListAsync(cancellationToken);

        var seenIps = new HashSet<string>(StringComparer.Ordinal);
        var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        foreach (var login in recentLogins)
        {
            if (!string.IsNullOrWhiteSpace(login.IpAddress) && seenIps.Add(login.IpAddress))
            {
                var ipMatches = await _db.Set<UserLoginLog>()
                    .AsNoTracking()
                    .Where(l => l.UserId != userId
                                && !l.IsDeleted
                                && l.IpAddress == login.IpAddress)
                    .Select(l => l.UserId)
                    .Distinct()
                    .Join(
                        _db.Set<User>().AsNoTracking()
                            .Where(u => !u.IsDeleted && !u.IsDeactivated),
                        id => id,
                        u => u.Id,
                        (_, u) => new LinkedAccountDto(u.SteamId, u.SteamDisplayName))
                    .ToListAsync(cancellationToken);

                if (ipMatches.Count > 0)
                {
                    signals.Add(new SupportingSignalDto(
                        Type: "IP_ADDRESS",
                        Value: login.IpAddress,
                        LinkedAccounts: ipMatches));
                }
            }

            if (!string.IsNullOrWhiteSpace(login.DeviceFingerprint)
                && seenFingerprints.Add(login.DeviceFingerprint))
            {
                var fpMatches = await _db.Set<UserLoginLog>()
                    .AsNoTracking()
                    .Where(l => l.UserId != userId
                                && !l.IsDeleted
                                && l.DeviceFingerprint == login.DeviceFingerprint)
                    .Select(l => l.UserId)
                    .Distinct()
                    .Join(
                        _db.Set<User>().AsNoTracking()
                            .Where(u => !u.IsDeleted && !u.IsDeactivated),
                        id => id,
                        u => u.Id,
                        (_, u) => new LinkedAccountDto(u.SteamId, u.SteamDisplayName))
                    .ToListAsync(cancellationToken);

                if (fpMatches.Count > 0)
                {
                    signals.Add(new SupportingSignalDto(
                        Type: "DEVICE_FINGERPRINT",
                        Value: login.DeviceFingerprint,
                        LinkedAccounts: fpMatches));
                }
            }
        }

        // ---------- Source-address signal (BUYER_PAYMENT.FromAddress) ----------
        // Only buyer-side payments carry a meaningful "source" — sellers receive
        // funds, they don't send them. The exchange/custodial allowlist is read
        // once per evaluation; "NONE" disables exclusion entirely (mirrors the
        // T30 auth.banned_countries pattern).
        var exchangeAddresses = await ReadExchangeAddressesAsync(cancellationToken);

        var sourceAddresses = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .Where(b => b.Type == BlockchainTransactionType.BUYER_PAYMENT
                        && b.Transaction.BuyerId == userId
                        && b.FromAddress != null
                        && b.FromAddress != "")
            .Select(b => b.FromAddress)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var addr in sourceAddresses)
        {
            if (string.IsNullOrWhiteSpace(addr) || exchangeAddresses.Contains(addr))
                continue;

            var matchUserIds = await _db.Set<BlockchainTransaction>()
                .AsNoTracking()
                .Where(b => b.Type == BlockchainTransactionType.BUYER_PAYMENT
                            && b.FromAddress == addr
                            && b.Transaction.BuyerId != null
                            && b.Transaction.BuyerId != userId)
                .Select(b => b.Transaction.BuyerId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (matchUserIds.Count == 0) continue;

            var matchedUsers = await _db.Set<User>()
                .AsNoTracking()
                .Where(u => matchUserIds.Contains(u.Id) && !u.IsDeleted && !u.IsDeactivated)
                .Select(u => new LinkedAccountDto(u.SteamId, u.SteamDisplayName))
                .ToListAsync(cancellationToken);

            if (matchedUsers.Count > 0)
            {
                signals.Add(new SupportingSignalDto(
                    Type: "SOURCE_ADDRESS",
                    Value: addr,
                    LinkedAccounts: matchedUsers));
            }
        }

        return signals;
    }

    private async Task<HashSet<string>> ReadExchangeAddressesAsync(
        CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == ExchangeAddressesSettingKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return MultiAccountSignalEvaluator.ParseExchangeAddresses(raw);
    }

    private sealed record LinkedAccountDto(string SteamId, string DisplayName);

    private sealed record SupportingSignalDto(
        string Type,
        string Value,
        IReadOnlyList<LinkedAccountDto> LinkedAccounts);
}
