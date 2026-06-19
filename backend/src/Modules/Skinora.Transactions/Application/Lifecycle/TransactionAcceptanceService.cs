using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
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
    private readonly IAccountFlagChecker _flagChecker;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;

    public TransactionAcceptanceService(
        AppDbContext db,
        ITrc20AddressValidator addressValidator,
        IWalletSanctionsCheck sanctions,
        IAccountFlagChecker flagChecker,
        IOutboxService outbox,
        TimeProvider clock)
    {
        _db = db;
        _addressValidator = addressValidator;
        _sanctions = sanctions;
        _flagChecker = flagChecker;
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

        // T105a: a suspended buyer cannot accept a transaction (incl. via open
        // link) — 02 §14.0 fund-flow restriction; treated as not-eligible at the guard.
        var buyer = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == buyerId && !u.IsDeleted && !u.IsDeactivated && !u.IsSuspended,
                cancellationToken);
        if (buyer is null)
            return Failure(AcceptTransactionStatus.BuyerNotFound,
                TransactionErrorCodes.AccountFlagged,
                "Buyer not found.");

        // WP4a — account-flag accept gate. 02 §14.0 (line 320): an account
        // flag blocks the flagged user's own fund-flow actions, explicitly
        // including "işlem kabul etme". This promotes the create-path seller
        // gate (TransactionEligibilityService.cs:56 → TransactionCreationService)
        // to the buyer/accept side, where the flag was previously never
        // consulted. Buyer-only by design (owner decision): a flagged account's
        // *own* accept is blocked; an existing active tx whose counterparty was
        // later flagged still continues (02 §14.0). Fail-fast here — after the
        // buyer is resolved but before the wallet/sanctions/cooldown work and
        // before the BuyerId/BuyerRefundAddress mutations (Stage 6) — so a
        // flagged accept makes no DB write (Failure() emits nothing).
        if (await _flagChecker.HasActiveAccountFlagAsync(buyerId, cancellationToken))
            return Failure(AcceptTransactionStatus.AccountFlagged,
                TransactionErrorCodes.AccountFlagged,
                "Account is flagged and cannot accept transactions (02 §14.0).");

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

        var previousStatus = transaction.Status;
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

        // WP15 — audit-trail row (06 §3.6). The buyer is the actor (USER).
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.BuyerAccept,
            ActorType.USER, buyerId, nowUtc);

        // WP12 (T90 K4) — the accept-time refund address is a PER-TRANSACTION
        // snapshot only. 02 §12.2 ("işlem bazlı adres") + 04 §7.3 ("yalnızca bu
        // işlem için geçerli adres değişikliği, profil adresi etkilenmez") +
        // 02 §12.3 snapshot prensibi: entering a different address while
        // accepting must NOT mutate the buyer's profile default
        // (User.DefaultRefundAddress) and must NOT start the profile-level
        // refund-address cooldown. The Stage-5 cooldown gate above still READS
        // the profile cooldown — an in-progress *profile* address change (T34
        // wallet flow) still blocks acceptance per 02 §12.3 — but acceptance
        // itself only fixes transaction.BuyerRefundAddress (Stage 6). The prior
        // T46 implementation wrote DefaultRefundAddress + RefundAddressChangedAt
        // here, which contradicted the snapshot principle and could lock the
        // buyer out of accepting other open invites within the cooldown window.

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

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // WP12 (T46) — OPEN_LINK first-comer race. Two concurrent accepts
            // both pass the CREATED state guard (Stage 2), both mutate the row
            // in their own tracked context, and both reach SaveChanges. The
            // RowVersion optimistic-concurrency token (AppDbContext
            // OnModelCreating) lets exactly one win; the loser's UPDATE matches
            // zero rows → DbUpdateConcurrencyException. Surface it as the same
            // 409 ALREADY_ACCEPTED the sequential guard (Stage 2) returns, not
            // an unhandled 500. Re-read the persisted status with a fresh
            // no-tracking query (the tracked entity is stale) and only swallow
            // when the row really did reach ACCEPTED — any other state is
            // unexpected and re-throws unchanged.
            var persistedStatus = await _db.Set<Transaction>()
                .AsNoTracking()
                .Where(t => t.Id == transactionId)
                .Select(t => (TransactionStatus?)t.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (persistedStatus == TransactionStatus.ACCEPTED)
                return Failure(AcceptTransactionStatus.AlreadyAccepted,
                    TransactionErrorCodes.AlreadyAccepted,
                    "Transaction has already been accepted.");
            throw;
        }

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
