using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Evaluates the eligibility envelope for <c>GET /transactions/eligibility</c>
/// (07 §7.3) and the pre-create gate inside <c>POST /transactions</c>.
/// Per 02 §11 mobile authenticator is sourced from the persisted
/// <c>User.MobileAuthenticatorVerified</c> flag (set by
/// <c>SteamTradeUrlService</c> at trade-URL save) — no live sidecar call,
/// matching the T33 profile read pattern.
///
/// <para>
/// 08 §2.2a added ONE live call: the Steam trade-eligibility check (limited
/// account + 15-day wait). It is not foldable into the persisted flag, because
/// that flag records the escrow hold and Steam reports a 0-second hold for an
/// account it forbids from trading entirely. Its transient failure is reported
/// as its own reason code rather than dropped — a check that quietly does not
/// run is the defect this gate exists to close.
/// </para>
/// </summary>
public sealed class TransactionEligibilityService : ITransactionEligibilityService
{
    private static readonly TransactionStatus[] _activeStatuses =
    [
        TransactionStatus.CREATED,
        TransactionStatus.FLAGGED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.SELLER_CONFIRMED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.ITEM_DELIVERED,
    ];

    private readonly AppDbContext _db;
    private readonly ITransactionLimitsProvider _limitsProvider;
    private readonly IAccountFlagChecker _flagChecker;
    private readonly ISteamTradeEligibilityChecker _steamTradeEligibility;
    private readonly TimeProvider _clock;

    public TransactionEligibilityService(
        AppDbContext db,
        ITransactionLimitsProvider limitsProvider,
        IAccountFlagChecker flagChecker,
        ISteamTradeEligibilityChecker steamTradeEligibility,
        TimeProvider clock)
    {
        _db = db;
        _limitsProvider = limitsProvider;
        _flagChecker = flagChecker;
        _steamTradeEligibility = steamTradeEligibility;
        _clock = clock;
    }

    public async Task<EligibilityDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && !u.IsDeactivated, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        var limits = await _limitsProvider.GetAsync(cancellationToken);
        var flagged = await _flagChecker.HasActiveAccountFlagAsync(userId, cancellationToken);

        var activeCount = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.SellerId == userId && _activeStatuses.Contains(t.Status))
            .CountAsync(cancellationToken);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var cooldownActive = user.CooldownExpiresAt.HasValue && user.CooldownExpiresAt.Value > nowUtc;
        var payoutCooldownActive = IsPayoutCooldownActive(user, limits, nowUtc);

        var (isNewAccount, currentNewAccountTx, maxNewAccountTx) =
            await EvaluateNewAccountLimitAsync(userId, user.CreatedAt, limits, nowUtc, cancellationToken);

        var concurrent = new EligibilityConcurrentLimit(
            Current: activeCount,
            Max: limits.MaxConcurrent ?? 0);

        var cancelCooldown = new EligibilityCancelCooldown(
            Active: cooldownActive,
            ExpiresAt: cooldownActive ? user.CooldownExpiresAt : null);

        var newAccount = new EligibilityNewAccountLimit(
            IsNewAccount: isNewAccount,
            Current: isNewAccount ? currentNewAccountTx : null,
            Max: isNewAccount ? maxNewAccountTx : null);

        var reasons = new List<string>();
        if (!user.MobileAuthenticatorVerified)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.MobileAuthenticatorRequired);
        if (flagged)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.AccountFlagged);
        if (cooldownActive)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.CancelCooldownActive);
        if (limits.MaxConcurrent.HasValue && activeCount >= limits.MaxConcurrent.Value)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.ConcurrentLimitReached);
        if (isNewAccount && maxNewAccountTx.HasValue && currentNewAccountTx >= maxNewAccountTx.Value)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.NewAccountLimitReached);
        if (string.IsNullOrEmpty(user.DefaultPayoutAddress))
            reasons.Add(TransactionErrorCodes.EligibilityReasons.SellerWalletAddressMissing);
        if (payoutCooldownActive)
            reasons.Add(TransactionErrorCodes.EligibilityReasons.PayoutAddressCooldownActive);

        // 08 §2.2a — the seller's own trade eligibility. Symmetric to the buyer
        // gates and open for exactly the same reason: a seller Steam blocks
        // from trading can list an item, take a buyer's escrowed payment and
        // then be unable to send anything.
        //
        // Evaluated UNCONDITIONALLY, like every other rule in this method: the
        // endpoint's contract is the complete reason list, so a seller who is
        // blocked by two things is told both at once instead of clearing one
        // and meeting the next. (An earlier revision of this comment claimed
        // the cheap database rules reject first and spare the Steam Community
        // request — they never did, there is no early return above. Reordering
        // would not save the quota either: the probe caches a CLEAN result for
        // 24h and never caches a restricted one, so the accounts that would
        // spend the quota are exactly the ones that keep spending it.)
        var steamEligibility = await _steamTradeEligibility.EvaluateAsync(user, cancellationToken);
        int? steamAccountRemainingDays = null;
        switch (steamEligibility.Status)
        {
            case SteamTradeEligibilityStatus.Limited:
                reasons.Add(TransactionErrorCodes.EligibilityReasons.SteamAccountLimited);
                break;
            case SteamTradeEligibilityStatus.TooNew:
                reasons.Add(TransactionErrorCodes.EligibilityReasons.SteamAccountTooNew);
                // The wait is the only gate here the seller can neither shorten
                // nor observe, so the number travels with the reason. Null for
                // every other status — a day count next to "limited account"
                // would name a deadline Steam never gave.
                steamAccountRemainingDays = steamEligibility.RemainingDays;
                break;
            case SteamTradeEligibilityStatus.Unknown:
                // A TRANSIENT reason, and the only one in this list. It is
                // reported rather than swallowed because swallowing it is
                // fail-open; the create path maps it to 503 so the caller is
                // told to retry instead of reading a permanent rejection.
                reasons.Add(TransactionErrorCodes.EligibilityReasons.SteamUnavailable);
                break;
        }

        return new EligibilityDto(
            Eligible: reasons.Count == 0,
            MobileAuthenticatorActive: user.MobileAuthenticatorVerified,
            ConcurrentLimit: concurrent,
            CancelCooldown: cancelCooldown,
            NewAccountLimit: newAccount,
            Reasons: reasons.Count == 0 ? null : reasons,
            SteamAccountRemainingDays: steamAccountRemainingDays);
    }

    private async Task<(bool IsNewAccount, int Current, int? Max)> EvaluateNewAccountLimitAsync(
        Guid userId,
        DateTime accountCreatedAt,
        TransactionLimits limits,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!limits.NewAccountPeriodDays.HasValue || !limits.NewAccountTransactionLimit.HasValue)
            return (false, 0, null);

        var periodEnd = accountCreatedAt.AddDays(limits.NewAccountPeriodDays.Value);
        if (nowUtc >= periodEnd)
            return (false, 0, null);

        // Per 02 §14.3 the new-account limit caps the *seller's* lifetime
        // transactions until the period elapses — completed + cancelled count
        // because the user has used their starter quota in either case.
        var startedCount = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.SellerId == userId)
            .CountAsync(cancellationToken);

        return (true, startedCount, limits.NewAccountTransactionLimit);
    }

    private static bool IsPayoutCooldownActive(User user, TransactionLimits limits, DateTime nowUtc)
    {
        if (!limits.PayoutAddressCooldownHours.HasValue) return false;
        if (!user.PayoutAddressChangedAt.HasValue) return false;
        var elapsed = nowUtc - user.PayoutAddressChangedAt.Value;
        return elapsed < TimeSpan.FromHours(limits.PayoutAddressCooldownHours.Value);
    }
}
