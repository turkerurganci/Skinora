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
/// </summary>
public sealed class TransactionEligibilityService : ITransactionEligibilityService
{
    private static readonly TransactionStatus[] _activeStatuses =
    [
        TransactionStatus.CREATED,
        TransactionStatus.FLAGGED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.ITEM_ESCROWED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
        TransactionStatus.ITEM_DELIVERED,
    ];

    private readonly AppDbContext _db;
    private readonly ITransactionLimitsProvider _limitsProvider;
    private readonly IAccountFlagChecker _flagChecker;
    private readonly TimeProvider _clock;

    public TransactionEligibilityService(
        AppDbContext db,
        ITransactionLimitsProvider limitsProvider,
        IAccountFlagChecker flagChecker,
        TimeProvider clock)
    {
        _db = db;
        _limitsProvider = limitsProvider;
        _flagChecker = flagChecker;
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

        return new EligibilityDto(
            Eligible: reasons.Count == 0,
            MobileAuthenticatorActive: user.MobileAuthenticatorVerified,
            ConcurrentLimit: concurrent,
            CancelCooldown: cancelCooldown,
            NewAccountLimit: newAccount,
            Reasons: reasons.Count == 0 ? null : reasons);
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
