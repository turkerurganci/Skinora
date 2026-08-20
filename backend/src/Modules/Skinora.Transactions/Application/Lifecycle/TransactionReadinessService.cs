using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Application.Settings;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T123 — 07 §7.6a / 03 §2.3 implementation. Drives
/// <c>ACCEPTED → SELLER_CONFIRMED</c>, arms <c>PaymentDeadline</c> and captures
/// the 02 §9.2 delivery baseline. Entity mutation, history row and outbox
/// publish land in a single <see cref="DbContext.SaveChangesAsync"/> so the
/// transition is atomic with the <c>TransactionStatusChangedEvent</c> the
/// buyer's "payment window open" notification rides on.
/// </summary>
public sealed class TransactionReadinessService : ITransactionReadinessService
{
    /// <summary>
    /// 06 §3.5 caps <c>BuyerBaselineAssetIds</c> at 400 characters. See
    /// <see cref="SerializeBaselineAssetIds"/> for what happens at the limit.
    /// </summary>
    private const int BaselineAssetIdsMaxLength = 400;

    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;
    private readonly ITradeUrlParser _tradeUrlParser;
    private readonly ITradeHoldChecker _tradeHoldChecker;
    private readonly ITimeoutSchedulingService _timeouts;
    private readonly IOutboxService _outbox;
    private readonly ILogger<TransactionReadinessService> _logger;
    private readonly TimeProvider _clock;

    public TransactionReadinessService(
        AppDbContext db,
        ISteamInventoryReader inventory,
        ITradeUrlParser tradeUrlParser,
        ITradeHoldChecker tradeHoldChecker,
        ITimeoutSchedulingService timeouts,
        IOutboxService outbox,
        ILogger<TransactionReadinessService> logger,
        TimeProvider clock)
    {
        _db = db;
        _inventory = inventory;
        _tradeUrlParser = tradeUrlParser;
        _tradeHoldChecker = tradeHoldChecker;
        _timeouts = timeouts;
        _outbox = outbox;
        _logger = logger;
        _clock = clock;
    }

    public async Task<ConfirmReadyOutcome> ConfirmReadyAsync(
        Guid sellerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        // ---------- Stage 1: load ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Failure(ConfirmReadyStatus.NotFound,
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: party guard (seller only — 07 §7.6a) ----------
        // Checked before the state guard so a stranger probing arbitrary ids
        // learns nothing about which state a transaction is in.
        if (transaction.SellerId != sellerId)
            return Failure(ConfirmReadyStatus.NotAParty,
                TransactionErrorCodes.NotAParty,
                "Only the seller can confirm readiness (07 §7.6a).");

        // ---------- Stage 3: state guard (ACCEPTED only) ----------
        if (transaction.Status != TransactionStatus.ACCEPTED)
            return Failure(ConfirmReadyStatus.InvalidStateTransition,
                TransactionErrorCodes.InvalidStateTransition,
                $"Cannot confirm readiness in state {transaction.Status} (05 §4.2).");

        // An emergency hold freezes every trigger (05 §4.5). The state machine
        // would reject this anyway at Stage 8 with the same error code — this
        // early exit only spares three Steam round-trips through a 1 req/s
        // queue for a transaction that cannot advance.
        if (transaction.IsOnHold)
            return Failure(ConfirmReadyStatus.InvalidStateTransition,
                TransactionStateMachine.OnHoldErrorCode,
                "Transaction is under emergency hold (05 §4.5).");

        var seller = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == transaction.SellerId, cancellationToken);
        if (seller is null || string.IsNullOrWhiteSpace(seller.SteamId))
            return Failure(ConfirmReadyStatus.SteamUnavailable,
                TransactionErrorCodes.SteamUnavailable,
                "The seller's Steam account could not be resolved.");

        // ---------- Stage 4: item still sendable (03 §2.3 step 3 md.1) -------
        // Read FRESH: the sidecar's 120-second cache entry may well be the one
        // this very seller warmed while browsing, and it would happily report
        // an item they traded away ninety seconds ago. This read is the gate
        // that decides whether the buyer is told to send money, so it must ask
        // Steam (08 §2.3 refresh).
        var lookup = await _inventory.GetItemAsync(
            seller.SteamId, transaction.ItemAssetId,
            InventoryReadFreshness.Fresh, cancellationToken);

        switch (lookup.Visibility)
        {
            // T121 — the three read outcomes are not interchangeable. Only a
            // Public read licenses "the item is gone"; the other two are
            // absence of information and get their own codes, so a seller whose
            // item is sitting untouched is never sent to look for it.
            case InventoryVisibility.Private:
                return Failure(ConfirmReadyStatus.InventoryPrivate,
                    TransactionErrorCodes.InventoryPrivate,
                    "The seller's Steam inventory is private, so the item could not be verified (08 §2.3).");
            case InventoryVisibility.Unavailable:
                return Failure(ConfirmReadyStatus.SteamUnavailable,
                    TransactionErrorCodes.SteamUnavailable,
                    "The seller's Steam inventory could not be read (08 §2.3).");
        }

        if (lookup.Item is null)
            return Failure(ConfirmReadyStatus.ItemNoLongerAvailable,
                TransactionErrorCodes.ItemNoLongerAvailable,
                "The item is no longer in the seller's Steam inventory (07 §7.6a).");
        if (!lookup.Item.IsTradeable)
            // Same code as "gone" by 07 §7.6a's own definition — from the
            // buyer's side an untradeable item is exactly as undeliverable as a
            // missing one, and 02 §9 rules out trade-locked items entirely.
            return Failure(ConfirmReadyStatus.ItemNoLongerAvailable,
                TransactionErrorCodes.ItemNoLongerAvailable,
                "The item is no longer tradeable (07 §7.6a).");

        // ---------- Stage 5: buyer's Mobile Authenticator (md.2) ----------
        var buyer = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == transaction.BuyerId, cancellationToken);
        if (buyer is null || string.IsNullOrWhiteSpace(buyer.SteamId))
            return Failure(ConfirmReadyStatus.SteamUnavailable,
                TransactionErrorCodes.SteamUnavailable,
                "The buyer's Steam account could not be resolved.");

        // The access token comes from the trade URL fixed at accept time
        // (07 §7.6 wrote the normalized form), which is also the address the
        // seller will actually send to — so the probe answers for the same pair
        // the delivery will use. A URL that no longer parses is treated as an
        // unavailable probe, never as "MA is off": the buyer would be blamed
        // for a stored-value defect they cannot see or fix.
        var parsedTradeUrl = _tradeUrlParser.Parse(transaction.BuyerTradeUrl);
        if (parsedTradeUrl is null)
        {
            _logger.LogError(
                "Transaction {TransactionId} carries an unparsable BuyerTradeUrl — "
                + "the trade-hold probe cannot run (06 §3.5)", transaction.Id);
            return Failure(ConfirmReadyStatus.SteamUnavailable,
                TransactionErrorCodes.SteamUnavailable,
                "The buyer's stored trade URL could not be read (08 §2.2).");
        }

        var hold = await _tradeHoldChecker.CheckAsync(
            buyer.SteamId, parsedTradeUrl.Token, cancellationToken);
        if (!hold.Available)
            // Fail-closed (08 §2.2). Same reasoning as the accept endpoint:
            // an unknown MA state could still drop the trade into Steam's
            // 15-day escrow, and reporting it as BUYER_MOBILE_AUTHENTICATOR_
            // INACTIVE would send the seller to chase a buyer whose
            // authenticator may be perfectly fine.
            return Failure(ConfirmReadyStatus.SteamUnavailable,
                TransactionErrorCodes.SteamUnavailable,
                "Steam could not be queried to verify the buyer's Mobile Authenticator (08 §2.2).");
        if (!hold.Active)
            return Failure(ConfirmReadyStatus.BuyerMobileAuthenticatorInactive,
                TransactionErrorCodes.BuyerMobileAuthenticatorInactive,
                "The buyer's Steam Mobile Authenticator is not active (02 §9.1).");

        // ---------- Stage 6: delivery baseline (md.3 — NON-blocking) --------
        // 03 §2.3 step 3 is explicit that an unreadable buyer inventory does not
        // stop the transaction: it closes the inventory-evidence path and leaves
        // buyer confirmation as the only route (02 §9.2). Blocking here would
        // punish both parties for the buyer's privacy setting.
        //
        // Fresh for a different reason than Stage 4: a cached snapshot is OLDER
        // than now, so any item the buyer acquired in that window is missing
        // from the baseline — and would later surface as a count delta, i.e. as
        // evidence of a delivery that never happened.
        var baseline = await _inventory.CaptureClassBaselineAsync(
            buyer.SteamId, transaction.ItemClassId, transaction.ItemInstanceId,
            InventoryReadFreshness.Fresh, cancellationToken);

        var buyerInventoryVisible = baseline.Visibility == InventoryVisibility.Public;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        if (buyerInventoryVisible)
        {
            transaction.BuyerBaselineClassCount = baseline.ClassCount;
            transaction.BuyerBaselineAssetIds = SerializeBaselineAssetIds(
                baseline.AssetIds, transaction.Id);

            // T130 — 06 §3.5 BuyerBaselineClassIds. Uncapped on purpose (see the
            // column config): this set is the reference the 03 §6.3 wrong-item
            // diff runs against, and a class dropped from it would later read as
            // an item that arrived after the snapshot.
            transaction.BuyerBaselineClassIds =
                JsonSerializer.Serialize(baseline.InventoryClassIds);

            transaction.BuyerBaselineCapturedAt = nowUtc;
        }
        else
        {
            // Left NULL on purpose — 06 §3.5: a NULL BuyerBaselineCapturedAt IS
            // the signal that the evidence path is closed. Writing zeros would
            // make an unread inventory indistinguishable from a genuinely empty
            // one, and the delta computed against that zero would read every
            // pre-existing copy as a fresh delivery.
            _logger.LogInformation(
                "Delivery baseline not captured for transaction {TransactionId} "
                + "(buyer inventory {Visibility}) — inventory evidence path closed (02 §9.2)",
                transaction.Id, baseline.Visibility);
        }

        // ---------- Stage 7: transition ----------
        // 06 §3.5 invariant: SellerReadyConfirmedAt must be set BEFORE the
        // state-machine guard fires (HasFieldsForSellerConfirmed).
        transaction.SellerReadyConfirmedAt = nowUtc;

        var previousStatus = transaction.Status;
        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.SellerConfirmReady);
        }
        catch (DomainException ex)
        {
            return Failure(ConfirmReadyStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // ---------- Stage 8: arm the payment window (05 §4.4) ----------
        // PaymentTimeoutMinutes was chosen by the seller at creation and
        // validated against the min/max SystemSettings there (07 §7.2), so it
        // needs no second lookup — the window the buyer was shown on the
        // listing is the window they get.
        var paymentDeadline = nowUtc + TimeSpan.FromMinutes(transaction.PaymentTimeoutMinutes);
        transaction.PaymentDeadline = paymentDeadline;

        // WP15 — audit-trail row (06 §3.6). The seller is the actor (USER).
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.SellerConfirmReady,
            ActorType.USER, sellerId, nowUtc);

        // ---------- Stage 9: Hangfire timeout jobs ----------
        // Called BEFORE the commit, per the 09 §13.3 contract on
        // ITimeoutSchedulingService: it writes the job ids onto the entity and
        // does NOT save, so job ids and the transition land in one
        // SaveChanges and a rollback discards both. The service re-reads the
        // transaction from this same DbContext, so it sees the tracked
        // SELLER_CONFIRMED + PaymentDeadline set two lines above.
        //
        // This is also the first production caller SchedulePaymentTimeoutAsync
        // has ever had — the custodial leg that used to arm the payment window
        // was deleted in T117.
        await _timeouts.SchedulePaymentTimeoutAsync(transaction.Id, cancellationToken);

        // ---------- Stage 10: outbox publish ----------
        // The generic status-changed event is the producer WP19's consumer has
        // been waiting for: it raises the buyer's PAYMENT_WINDOW_OPEN
        // notification carrying the deposit address, and feeds the WP9 realtime
        // relay. Published inside the same SaveChanges as the transition so the
        // buyer is never told to pay for a transition that rolled back.
        await _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: previousStatus,
                ToStatus: transaction.Status,
                OccurredAt: nowUtc),
            cancellationToken);

        // ---------- Stage 10b: arm the payment monitor (T139) ---------------
        // The deposit address has existed since creation, but nothing was ever
        // watching it: the sidecar's active monitor (T71) had no backend caller
        // at all until T139 (DEFERRED_BACKLOG T133b-PaymentMonitorUnarmed), so
        // a real buyer's transfer produced no payment-detected webhook and the
        // transaction sat here until it timed out. SELLER_CONFIRMED is the
        // right moment to arm: it is the first state in which the buyer is
        // shown the address (02 §2.2 step 3), hence the first state in which
        // money can arrive.
        //
        // Published in the same SaveChanges as the transition for the same
        // reason the status event is: a rolled-back confirmation must not leave
        // a monitor armed on an address the buyer was never told about.
        //
        // A missing PaymentAddress row does NOT block the confirmation.
        // Allocation is best-effort at creation and swept by
        // EnsurePaymentAddressJob (T70/T123); EnsurePaymentMonitorJob arms
        // whatever exists a minute later. Blocking here would punish the seller
        // for a sidecar outage whose only real effect is on the buyer's ability
        // to pay — and the payment deadline armed in Stage 8 is what protects
        // them if the address never appears.
        var paymentAddress = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.TransactionId == transaction.Id && !p.IsDeleted,
                cancellationToken);

        if (paymentAddress is not null)
        {
            await _outbox.PublishAsync(
                new PaymentMonitorStartRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    PaymentAddressId: paymentAddress.Id,
                    Address: paymentAddress.Address,
                    ExpectedToken: paymentAddress.ExpectedToken,
                    ExpectedContractAddress: KnownStablecoinContracts
                        .ResolveContractAddress(paymentAddress.ExpectedToken),
                    OccurredAt: nowUtc),
                cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Transaction {TransactionId} reached SELLER_CONFIRMED without a PaymentAddress "
                + "row — the payment monitor was not armed inline. EnsurePaymentMonitorJob will "
                + "arm it once EnsurePaymentAddressJob allocates the address (T139).",
                transaction.Id);
        }

        // Single unit of work: transition + baseline + deadline + Hangfire job
        // ids + history row + both outbox messages.
        await _db.SaveChangesAsync(cancellationToken);

        return new ConfirmReadyOutcome(
            ConfirmReadyStatus.Confirmed,
            new ConfirmReadyResponse(
                Status: transaction.Status,
                SellerReadyConfirmedAt: transaction.SellerReadyConfirmedAt!.Value,
                PaymentDeadline: paymentDeadline,
                BuyerInventoryVisible: buyerInventoryVisible),
            ErrorCode: null,
            ErrorMessage: null);
    }

    /// <summary>
    /// Serialize the baseline asset IDs into the 400-character
    /// <c>BuyerBaselineAssetIds</c> column (06 §3.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Realistic inventories fit comfortably: an asset ID is ~11 digits, so the
    /// column holds roughly 28 of them and T122 measured the busiest single
    /// class in a real inventory at 9 copies. A collector who exceeds that is
    /// nevertheless possible, and an oversized string would throw at
    /// <c>SaveChanges</c> — turning a legitimate confirmation into a 500.
    /// </para>
    /// <para>
    /// So the list is truncated to what fits, loudly. This degrades one thing
    /// only: <c>WRONG_ITEM</c> dispute handling, which uses the ID list to tell
    /// a newly-arrived asset from a pre-existing one (02 §10.1). It does
    /// <em>not</em> weaken delivery verification itself — that is decided by
    /// <c>BuyerBaselineClassCount</c>, which is always the true, untruncated
    /// count (02 §9.2 is a counting rule, not a set-membership rule).
    /// </para>
    /// </remarks>
    private string SerializeBaselineAssetIds(IReadOnlyList<string> assetIds, Guid transactionId)
    {
        var serialized = JsonSerializer.Serialize(assetIds);
        if (serialized.Length <= BaselineAssetIdsMaxLength) return serialized;

        var kept = new List<string>(assetIds.Count);
        foreach (var assetId in assetIds)
        {
            kept.Add(assetId);
            if (JsonSerializer.Serialize(kept).Length > BaselineAssetIdsMaxLength)
            {
                kept.RemoveAt(kept.Count - 1);
                break;
            }
        }

        _logger.LogWarning(
            "Baseline asset-id list for transaction {TransactionId} exceeded the 06 §3.5 "
            + "{MaxLength}-character column: {Kept} of {Total} ids stored. ClassCount stays "
            + "exact, so delivery verification is unaffected; WRONG_ITEM asset discrimination "
            + "is degraded for this transaction (02 §10.1).",
            transactionId, BaselineAssetIdsMaxLength, kept.Count, assetIds.Count);

        return JsonSerializer.Serialize(kept);
    }

    private static ConfirmReadyOutcome Failure(
        ConfirmReadyStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
