using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T46 — 07 §7.6, 03 §3.2 implementation. All side effects (entity update,
/// outbox publish, <c>User.RefundAddressChangedAt</c>) land inside a single
/// <see cref="DbContext.SaveChangesAsync"/> so the
/// <c>CREATED → ACCEPTED</c> transition is atomic with the
/// <c>BuyerAcceptedEvent</c> emission.
/// </summary>
public sealed class TransactionAcceptanceService : ITransactionAcceptanceService
{
    /// <summary>SystemSetting key for the buyer refund-address cooldown (02 §12.3).</summary>
    public const string RefundCooldownKey = "wallet.refund_address_cooldown_hours";

    private readonly AppDbContext _db;
    private readonly ITrc20AddressValidator _addressValidator;
    private readonly IWalletSanctionsCheck _sanctions;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;

    public TransactionAcceptanceService(
        AppDbContext db,
        ITrc20AddressValidator addressValidator,
        IWalletSanctionsCheck sanctions,
        IOutboxService outbox,
        TimeProvider clock)
    {
        _db = db;
        _addressValidator = addressValidator;
        _sanctions = sanctions;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<AcceptTransactionOutcome> AcceptAsync(
        Guid buyerId,
        Guid transactionId,
        AcceptTransactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RefundWalletAddress))
            return Failure(AcceptTransactionStatus.ValidationFailed,
                TransactionErrorCodes.RefundAddressRequired,
                "refundWalletAddress is required (07 §7.6).");

        // ---------- Stage 1: load transaction + buyer ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Failure(AcceptTransactionStatus.NotFound,
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        var buyer = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == buyerId && !u.IsDeleted && !u.IsDeactivated,
                cancellationToken);
        if (buyer is null)
            return Failure(AcceptTransactionStatus.BuyerNotFound,
                TransactionErrorCodes.AccountFlagged,
                "Buyer not found.");

        // ---------- Stage 2: state guard (CREATED only) ----------
        if (transaction.Status == TransactionStatus.ACCEPTED)
            return Failure(AcceptTransactionStatus.AlreadyAccepted,
                TransactionErrorCodes.AlreadyAccepted,
                "Transaction has already been accepted.");
        if (transaction.Status != TransactionStatus.CREATED)
            return Failure(AcceptTransactionStatus.InvalidStateTransition,
                TransactionErrorCodes.InvalidStateTransition,
                $"Cannot accept transaction in state {transaction.Status} (05 §4.2).");

        // ---------- Stage 3: party guard (Yöntem 1 / Yöntem 2 — 02 §6) ----------
        if (transaction.BuyerIdentificationMethod == BuyerIdentificationMethod.STEAM_ID)
        {
            // 02 §6.1: only the explicitly invited Steam ID can accept.
            // 03 §3.2: mismatch surfaces as STEAM_ID_MISMATCH (07 §7.6).
            if (!string.Equals(buyer.SteamId, transaction.TargetBuyerSteamId,
                    StringComparison.Ordinal))
                return Failure(AcceptTransactionStatus.SteamIdMismatch,
                    TransactionErrorCodes.SteamIdMismatch,
                    "Caller's Steam ID does not match the invited buyer.");
        }
        else // OPEN_LINK
        {
            // 02 §6.2: first-comer wins; the seller cannot accept their own
            // listing. Single-use semantics are enforced by the CREATED state
            // guard above (subsequent calls hit ALREADY_ACCEPTED or INVALID_STATE).
            if (transaction.SellerId == buyerId)
                return Failure(AcceptTransactionStatus.NotAParty,
                    TransactionErrorCodes.NotAParty,
                    "Seller cannot accept their own listing (02 §6.2).");
        }

        // ---------- Stage 4: refund wallet pipeline (02 §12.3) ----------
        if (!_addressValidator.IsValid(request.RefundWalletAddress))
            return Failure(AcceptTransactionStatus.InvalidWallet,
                TransactionErrorCodes.InvalidWalletAddress,
                "refundWalletAddress fails TRC-20 validation (02 §12.3).");

        var sanctions = await _sanctions.EvaluateAsync(request.RefundWalletAddress, cancellationToken);
        if (sanctions.IsMatch)
            return Failure(AcceptTransactionStatus.SanctionsMatch,
                TransactionErrorCodes.SanctionsMatch,
                $"refundWalletAddress matched sanctions list '{sanctions.MatchedList}'.");

        // ---------- Stage 5: refund-address cooldown (02 §12.3) ----------
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var cooldownHours = await ReadRefundCooldownHoursAsync(cancellationToken);
        if (IsRefundCooldownActive(buyer, cooldownHours, nowUtc))
            return Failure(AcceptTransactionStatus.WalletCooldownActive,
                TransactionErrorCodes.WalletChangeCooldownActive,
                "Refund-address cooldown is active (02 §12.3).");

        // ---------- Stage 6: state transition + snapshot ----------
        // 06 §3.5 invariants: BuyerId + BuyerRefundAddress must be set BEFORE
        // the state-machine guard fires (HasFieldsForAccepted).
        transaction.BuyerId = buyerId;
        transaction.BuyerRefundAddress = request.RefundWalletAddress;

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.BuyerAccept);
        }
        catch (DomainException ex)
        {
            return Failure(AcceptTransactionStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // 02 §12.3 — accepting the transaction sets the refund-address change
        // timestamp so the cooldown window starts ticking from this moment
        // for the buyer (mirrors T34 wallet-change semantics for the wire path).
        if (!string.Equals(buyer.DefaultRefundAddress, request.RefundWalletAddress, StringComparison.Ordinal))
        {
            buyer.DefaultRefundAddress = request.RefundWalletAddress;
            buyer.RefundAddressChangedAt = nowUtc;
        }

        // ---------- Stage 7: outbox publish (T62/T78–T80 consume) ----------
        await _outbox.PublishAsync(
            new BuyerAcceptedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: transaction.SellerId,
                BuyerId: buyerId,
                ItemName: transaction.ItemName,
                AcceptedAt: transaction.AcceptedAt!.Value,
                OccurredAt: nowUtc),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new AcceptTransactionOutcome(
            AcceptTransactionStatus.Accepted,
            new AcceptTransactionResponse(
                Status: transaction.Status,
                AcceptedAt: transaction.AcceptedAt!.Value),
            ErrorCode: null,
            ErrorMessage: null);
    }

    private async Task<int?> ReadRefundCooldownHoursAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == RefundCooldownKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
            return parsed;
        return null;
    }

    private static bool IsRefundCooldownActive(User buyer, int? cooldownHours, DateTime nowUtc)
    {
        if (!cooldownHours.HasValue || !buyer.RefundAddressChangedAt.HasValue) return false;
        return nowUtc - buyer.RefundAddressChangedAt.Value < TimeSpan.FromHours(cooldownHours.Value);
    }

    private static AcceptTransactionOutcome Failure(
        AcceptTransactionStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
