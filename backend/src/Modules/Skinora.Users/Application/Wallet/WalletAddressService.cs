using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;
using Skinora.Users.Application.MultiAccount;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Wallet;

public sealed class WalletAddressService : IWalletAddressService
{
    private readonly AppDbContext _db;
    private readonly IWalletAddressValidator _addressValidator;
    private readonly IWalletSanctionsCheck _sanctions;
    private readonly ISanctionsViolationHandler _sanctionsViolation;
    private readonly IActiveTransactionCounter _activeCounter;
    private readonly IMultiAccountDetector _multiAccountDetector;
    private readonly TimeProvider _clock;
    private readonly ILogger<WalletAddressService> _logger;

    public WalletAddressService(
        AppDbContext db,
        IWalletAddressValidator addressValidator,
        IWalletSanctionsCheck sanctions,
        ISanctionsViolationHandler sanctionsViolation,
        IActiveTransactionCounter activeCounter,
        IMultiAccountDetector multiAccountDetector,
        TimeProvider clock,
        ILogger<WalletAddressService> logger)
    {
        _db = db;
        _addressValidator = addressValidator;
        _sanctions = sanctions;
        _sanctionsViolation = sanctionsViolation;
        _activeCounter = activeCounter;
        _multiAccountDetector = multiAccountDetector;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WalletUpdateResult> UpdateWalletAsync(
        Guid userId,
        WalletRole role,
        BlockchainNetwork network,
        string? newAddress,
        bool confirmedWithoutChecksum,
        bool reAuthValidated,
        CancellationToken cancellationToken)
    {
        // ---------- (1) Format, by network (02 §12.3, 07 §1.3) ----------
        var validation = _addressValidator.Validate(network, newAddress);
        if (!validation.IsUsable)
            return WalletUpdateResult.Failure(
                WalletUpdateStatus.InvalidAddress, network, reason: validation.Reason);

        // An EVM address with no EIP-55 checksum is stored only on the user's
        // explicit second confirmation — a typo in it cannot be detected.
        var checksumVerified = validation.Status == WalletAddressValidationStatus.Valid;
        if (!checksumVerified && !confirmedWithoutChecksum)
            return WalletUpdateResult.Failure(
                WalletUpdateStatus.ChecksumConfirmationRequired, network, reason: validation.Reason);

        // Canonical form (06 §1.4): EVM lowercase, TRON unchanged — what the
        // column stores, so every comparison below matches it.
        var candidate = validation.CanonicalAddress!;
        var type = role.ToAddressType();

        // T105a: a suspended user cannot change payout/refund addresses
        // (defense-in-depth for a fraud-suspended account — prevents redirecting
        // funds while under review). Treated as not-eligible at the guard.
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated && !u.IsSuspended, cancellationToken);

        if (user is null)
            return WalletUpdateResult.Failure(WalletUpdateStatus.UserNotFound, network);

        // One row per (user, network, type) — 06 §1.4. Every rule below is
        // scoped to it: another network's address of the same type neither
        // demands re-auth nor is affected by this change.
        var row = await _db.Set<UserWalletAddress>()
            .FirstOrDefaultAsync(
                w => w.UserId == userId && w.Network == network && w.Type == type,
                cancellationToken);

        var previous = row?.Address;
        var previousChangedAt = row?.ChangedAt;

        // 02 §12.3 / 07 §5.3 "Ek Auth": changing an existing address requires
        // a valid X-ReAuth-Token. The controller consumes the token and passes
        // the outcome via reAuthValidated — see UsersController.
        if (!string.IsNullOrEmpty(previous) && !reAuthValidated)
            return WalletUpdateResult.Failure(WalletUpdateStatus.ReAuthRequired, network);

        // ---------- (2) Sanctions, on this network (02 §21.1, 06 §3.25) ----------
        var sanctions = await _sanctions.EvaluateAsync(network, candidate, cancellationToken);
        if (sanctions.IsMatch)
        {
            // T82 — 02 §21.1, 03 §11a.3: yeni adres reddedilir + hesap
            // flag'lenir + aktif işlemler EMERGENCY_HOLD'a alınır. Aday
            // adres saklanmaz (UserWalletAddress satırı değişmez); ihlal
            // kaydı flag.details içine yazılır.
            await _sanctionsViolation.RecordWalletAttemptAsync(
                userId, candidate, sanctions.MatchedList ?? "UNKNOWN", cancellationToken);

            return WalletUpdateResult.Failure(
                WalletUpdateStatus.SanctionsMatch, network, sanctions.MatchedList);
        }

        var replacing = !string.IsNullOrEmpty(previous) && !BlockchainAddress.AreEqual(previous, candidate);

        var activeUsingOld = 0;
        if (replacing)
        {
            activeUsingOld = await _activeCounter.CountActiveUsingAddressAsync(
                userId, role, previous!, cancellationToken);
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // T105b — record the superseded address to the append-only wallet
        // history before overwriting it (04 §8.9.3 "önceki adresler"). Only an
        // actual replacement of an existing, different address is recorded — a
        // first-time set (no row yet) or a no-op re-save (same address) leaves
        // no history row. Inserted in the same SaveChanges as the address row,
        // so the two commit atomically.
        if (replacing)
        {
            _db.Set<WalletAddressHistory>().Add(new WalletAddressHistory
            {
                UserId = user.Id,
                Network = network,
                Type = role.ToHistoryType(),
                Address = previous!,
                SetAt = previousChangedAt,
                CreatedAt = now,
            });
        }

        if (row is null)
        {
            row = UserWalletAddress.For(network, type, candidate);
            row.UserId = user.Id;
            _db.Set<UserWalletAddress>().Add(row);
        }

        // The cooldown window (02 §12.3) restarts on every save, a no-op
        // re-save included — unchanged from the pre-F8 single-address flow.
        row.Address = candidate;
        row.ChangedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        // T56 — multi-account detection. Runs after the wallet update commits
        // so the cross-account query sees the new address. Failures are logged
        // and swallowed so a transient detector error never rolls back a valid
        // wallet update; the next change picks up any missed signal.
        try
        {
            await _multiAccountDetector.EvaluateAsync(userId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Multi-account detection failed after wallet update for user {UserId}; wallet change persisted.",
                userId);
        }

        return WalletUpdateResult.Success(
            network,
            BlockchainAddress.ToDisplay(candidate)!,
            now,
            activeUsingOld,
            checksumVerified);
    }
}
