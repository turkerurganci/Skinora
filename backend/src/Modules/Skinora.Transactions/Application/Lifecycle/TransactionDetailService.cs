using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// T46 — 07 §7.5 read-path implementation. Single round-trip with
/// <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>;
/// resolves both party records via a separate batched lookup. Conditional
/// sections (payment, sellerPayout, refund, dispute, holdInfo, flagInfo,
/// cancelInfo, paymentEvents, invitedInfo) are surfaced when the entity
/// state allows it; downstream tasks (T47/T49/T51/T54/T58/T59/T70+) fill
/// the remaining branches.
/// </summary>
public sealed class TransactionDetailService : ITransactionDetailService
{
    private const string TronNetworkLabel = "Tron (TRC-20)";

    // States where the buyer may still cancel — i.e. before their money moves.
    // The seller's cancel window is wider (it includes PAYMENT_RECEIVED), so it
    // is evaluated separately in BuildAvailableActions (02 §7).
    private static readonly TransactionStatus[] _activeStatesForBuyerCancel =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.SELLER_CONFIRMED,
    ];

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public TransactionDetailService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TransactionDetailOutcome> GetAsync(
        Guid transactionId,
        Guid? callerId,
        string? callerSteamId,
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);

        if (transaction is null)
            return new TransactionDetailOutcome(
                TransactionDetailStatus.NotFound,
                Body: null,
                ErrorCode: TransactionErrorCodes.TransactionNotFound,
                ErrorMessage: "Transaction not found.");

        // Resolve role (seller / buyer / non-party).
        // Non-party authenticated callers receive 403 NOT_A_PARTY (07 §7.5
        // hatalar). Public (callerId == null) callers receive the trimmed
        // public shape regardless of party. The "target buyer before
        // acceptance" case (02 §6.1, 03 §3.2 step 1) — STEAM_ID method, the
        // invited Steam ID can view the detail prior to accepting — is
        // resolved by Steam-ID match when BuyerId is still null.
        string? role = null;
        if (callerId.HasValue)
        {
            if (callerId.Value == transaction.SellerId)
            {
                role = "seller";
            }
            else if (callerId.Value == transaction.BuyerId)
            {
                role = "buyer";
            }
            else if (transaction.BuyerId is null
                && transaction.BuyerIdentificationMethod == BuyerIdentificationMethod.STEAM_ID
                && !string.IsNullOrEmpty(callerSteamId)
                && string.Equals(callerSteamId, transaction.TargetBuyerSteamId, StringComparison.Ordinal))
            {
                role = "buyer";
            }
            else
            {
                return new TransactionDetailOutcome(
                    TransactionDetailStatus.NotAParty,
                    Body: null,
                    ErrorCode: TransactionErrorCodes.NotAParty,
                    ErrorMessage: "Caller is not a party to this transaction.");
            }
        }

        return await BuildResponseAsync(transaction, role, prospectiveBuyer: false, cancellationToken);
    }

    public async Task<TransactionDetailOutcome> GetByInviteTokenAsync(
        string inviteToken,
        Guid? callerId,
        string? callerSteamId,
        CancellationToken cancellationToken)
    {
        // Defensive: an empty token would translate to "InviteToken IS NULL"
        // and match STEAM_ID rows — short-circuit to NotFound instead.
        if (string.IsNullOrEmpty(inviteToken))
            return new TransactionDetailOutcome(
                TransactionDetailStatus.NotFound,
                Body: null,
                ErrorCode: TransactionErrorCodes.TransactionNotFound,
                ErrorMessage: "Invitation not found.");

        var transaction = await _db.Set<Transaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.InviteToken == inviteToken && !t.IsDeleted,
                cancellationToken);

        if (transaction is null)
            return new TransactionDetailOutcome(
                TransactionDetailStatus.NotFound,
                Body: null,
                ErrorCode: TransactionErrorCodes.TransactionNotFound,
                ErrorMessage: "Invitation not found.");

        // Invite role resolution (differs from the id path). The token is the
        // access grant: an authenticated holder who is not yet a party is a
        // prospective buyer while the invite is still joinable (CREATED, no
        // buyer) — 02 §6.2 first-comer. Spent / already-accepted invites fall
        // back to the trimmed public shape for non-parties so the FE renders
        // an "unavailable" surface instead of a bare 403.
        string? role;
        var prospectiveBuyer = false;
        if (!callerId.HasValue)
        {
            role = null;
        }
        else if (callerId.Value == transaction.SellerId)
        {
            role = "seller";
        }
        else if (callerId.Value == transaction.BuyerId)
        {
            role = "buyer";
        }
        else if (transaction.Status == TransactionStatus.CREATED && transaction.BuyerId is null)
        {
            role = "buyer";
            prospectiveBuyer = true;
        }
        else
        {
            role = null;
        }

        return await BuildResponseAsync(transaction, role, prospectiveBuyer, cancellationToken);
    }

    private async Task<TransactionDetailOutcome> BuildResponseAsync(
        Transaction transaction,
        string? role,
        bool prospectiveBuyer,
        CancellationToken cancellationToken)
    {
        // Single batched party lookup. Buyer FK is nullable until the buyer
        // accepts; we still want their snapshot once they do.
        var partyIds = new List<Guid> { transaction.SellerId };
        if (transaction.BuyerId.HasValue) partyIds.Add(transaction.BuyerId.Value);
        var parties = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => partyIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.SteamId,
                u.SteamDisplayName,
                u.SteamAvatarUrl,
                u.SuccessfulTransactionRate,
                u.CompletedTransactionCount,
            })
            .ToListAsync(cancellationToken);

        var sellerRow = parties.First(p => p.Id == transaction.SellerId);
        var buyerRow = transaction.BuyerId.HasValue
            ? parties.FirstOrDefault(p => p.Id == transaction.BuyerId.Value)
            : null;

        if (role is null)
        {
            return BuildPublicResponse(transaction, sellerRow.SteamDisplayName);
        }

        // Authenticated view — full surface, role-specific availableActions.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var sellerParty = new TransactionPartyDto(
            SteamId: sellerRow.SteamId,
            DisplayName: sellerRow.SteamDisplayName,
            AvatarUrl: sellerRow.SteamAvatarUrl,
            ReputationScore: ComputeReputation(sellerRow.SuccessfulTransactionRate),
            CompletedTransactionCount: sellerRow.CompletedTransactionCount);

        TransactionPartyDto? buyerParty = null;
        if (buyerRow is not null
            && transaction.Status != TransactionStatus.CREATED
            && transaction.Status != TransactionStatus.FLAGGED)
        {
            buyerParty = new TransactionPartyDto(
                SteamId: buyerRow.SteamId,
                DisplayName: buyerRow.SteamDisplayName,
                AvatarUrl: buyerRow.SteamAvatarUrl,
                ReputationScore: ComputeReputation(buyerRow.SuccessfulTransactionRate),
                CompletedTransactionCount: buyerRow.CompletedTransactionCount);
        }

        var item = new TransactionItemDto(
            AssetId: transaction.ItemAssetId,
            Name: transaction.ItemName,
            Type: transaction.ItemType,
            ImageUrl: transaction.ItemIconUrl,
            Wear: transaction.ItemExterior);

        var warningPercent = await TimeoutWarningThreshold.ReadPercentAsync(_db, cancellationToken);
        var timeout = BuildTimeout(transaction, nowUtc, warningPercent);

        // 04 §7 — in PAYMENT_RECEIVED the seller must send the item directly to
        // the buyer, so the CTA opens the buyer's own trade URL. No lookup is
        // needed: the platform no longer creates trade offers, it just hands the
        // seller a ready link (02 §2.2 step 6). Shown to the seller only — the
        // buyer has no action to take on Steam.
        var tradeOfferUrl = transaction.Status == TransactionStatus.PAYMENT_RECEIVED && role == "seller"
            ? transaction.BuyerTradeUrl
            : null;

        InviteInfoDto? invite = null;
        if (role == "seller"
            && transaction.Status == TransactionStatus.CREATED
            && transaction.BuyerId is null)
        {
            // 02 §6.1: registered Steam buyers get the public path; only
            // OPEN_LINK transactions surface the opaque invite URL.
            var url = transaction.InviteToken is null
                ? $"/transactions/{transaction.Id:D}"
                : $"/invite/{transaction.InviteToken}";
            invite = new InviteInfoDto(
                InviteUrl: url,
                BuyerRegistered: false,
                BuyerNotified: false);
        }

        FlagInfoDto? flag = null;
        if (transaction.Status == TransactionStatus.FLAGGED)
        {
            flag = new FlagInfoDto(
                FlagType: "PRICE_DEVIATION",
                Message: "İşleminiz incelemeye alındı. Sonuç size bildirilecektir.");
        }

        CancelInfoDto? cancel = null;
        if (transaction.CancelledAt.HasValue && transaction.CancelledBy.HasValue)
        {
            cancel = new CancelInfoDto(
                CancelledBy: transaction.CancelledBy.Value.ToString(),
                Reason: transaction.CancelReason ?? string.Empty,
                CancelledAt: transaction.CancelledAt.Value,
                PaymentRefunded: transaction.PaymentReceivedAt.HasValue);
        }

        HoldInfoDto? hold = null;
        if (transaction.IsOnHold)
        {
            var prev = transaction.PreviousStatusBeforeHold.HasValue
                ? ((TransactionStatus)transaction.PreviousStatusBeforeHold.Value).ToString()
                : transaction.Status.ToString();
            hold = new HoldInfoDto(
                PreviousStatus: prev,
                Reason: transaction.EmergencyHoldReason ?? string.Empty,
                FrozenAt: transaction.EmergencyHoldAt ?? transaction.TimeoutFrozenAt ?? nowUtc,
                Message: "İşleminiz güvenlik incelemesi nedeniyle donduruldu. Süreç admin tarafından yönetilmektedir.");
        }

        // Prospective buyers (token holder, not yet a party) only get
        // canAccept — cancel/dispute belong to actual parties. Accept itself
        // stays id-based (POST /transactions/:id/accept), where the
        // acceptance service enforces the 02 §6.2 first-comer guard.
        var actions = prospectiveBuyer
            ? new AvailableActionsDto(
                CanAccept: !transaction.IsOnHold,
                CanConfirmReady: null,
                CanConfirmReceipt: null,
                CanCancel: null,
                CanDispute: null,
                DisputableTypes: null,
                CanEscalate: null,
                RequiresLogin: null)
            : BuildAuthenticatedActions(transaction, role!, nowUtc);

        var sellerPayout = await BuildSellerPayoutAsync(transaction, role, cancellationToken);
        var refund = await BuildRefundAsync(transaction, role, cancellationToken);

        var dto = new TransactionDetailDto(
            Id: transaction.Id,
            Status: ProjectStatus(transaction),
            UserRole: role,
            Item: item,
            Price: FormatMoney(transaction.Price),
            Stablecoin: transaction.StablecoinType,
            CommissionRate: transaction.CommissionRate,
            CommissionAmount: FormatMoney(transaction.CommissionAmount),
            TotalAmount: FormatMoney(transaction.TotalAmount),
            Seller: sellerParty,
            Buyer: buyerParty,
            Timeout: timeout,
            Payment: null,           // T70+ blockchain monitoring
            SellerPayout: sellerPayout, // WP1 — COMPLETED seller view (07 §7.5)
            Refund: refund,          // WP2 — buyer payment-refund view (07 §7.5)
            CancelInfo: cancel,
            FlagInfo: flag,
            HoldInfo: hold,
            Dispute: null,           // T58 dispute
            InviteInfo: invite,
            PaymentEvents: ProducePaymentEventsArray(transaction),
            DeliveredBuyerAssetId: transaction.DeliveredBuyerAssetId,
            SteamTradeOfferUrl: tradeOfferUrl,
            AvailableActions: actions,
            CreatedAt: transaction.CreatedAt,
            UpdatedAt: transaction.UpdatedAt);

        return new TransactionDetailOutcome(
            TransactionDetailStatus.Found,
            Body: dto,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private TransactionDetailOutcome BuildPublicResponse(
        Transaction transaction,
        string sellerDisplayName)
    {
        // 07 §7.5 public sample: id, status, userRole=null, minimal item,
        // price, stablecoin, seller (display name only), availableActions
        // with requiresLogin=true.
        var dto = new TransactionDetailDto(
            Id: transaction.Id,
            Status: ProjectStatus(transaction),
            UserRole: null,
            Item: new TransactionItemDto(
                AssetId: null,
                Name: transaction.ItemName,
                Type: null,
                ImageUrl: transaction.ItemIconUrl,
                Wear: null),
            Price: FormatMoney(transaction.Price),
            Stablecoin: transaction.StablecoinType,
            CommissionRate: null,
            CommissionAmount: null,
            TotalAmount: null,
            Seller: new TransactionPartyDto(
                SteamId: null,
                DisplayName: sellerDisplayName,
                AvatarUrl: null,
                ReputationScore: null,
                CompletedTransactionCount: null),
            Buyer: null,
            Timeout: null,
            Payment: null,
            SellerPayout: null,
            Refund: null,
            CancelInfo: null,
            FlagInfo: null,
            HoldInfo: null,
            Dispute: null,
            InviteInfo: null,
            PaymentEvents: null,
            DeliveredBuyerAssetId: null,
            SteamTradeOfferUrl: null,
            AvailableActions: new AvailableActionsDto(
                CanAccept: false,
                CanConfirmReady: null,
                CanConfirmReceipt: null,
                CanCancel: null,
                CanDispute: null,
                DisputableTypes: null,
                CanEscalate: null,
                RequiresLogin: true),
            CreatedAt: null,
            UpdatedAt: null);

        return new TransactionDetailOutcome(
            TransactionDetailStatus.Found,
            Body: dto,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static IReadOnlyList<PaymentEventDto>? ProducePaymentEventsArray(Transaction transaction)
    {
        // 07 §7.5: paymentEvents available from SELLER_CONFIRMED+ — that is the
        // first state where a deposit address exists and money can arrive.
        // Before then an empty array would imply "we have data, none to show";
        // we return null so the field is suppressed entirely.
        if (transaction.Status == TransactionStatus.CREATED
            || transaction.Status == TransactionStatus.FLAGGED
            || transaction.Status == TransactionStatus.ACCEPTED)
            return null;
        return Array.Empty<PaymentEventDto>();
    }

    private static TransactionTimeoutDto? BuildTimeout(Transaction transaction, DateTime nowUtc, int warningPercent)
    {
        // 07 §7.5 timeout block — surfaced when an active deadline is in
        // play. Terminal states (COMPLETED, CANCELLED_*) hide the block.
        if (IsTerminal(transaction.Status)) return null;

        var (type, expiresAt) = transaction.Status switch
        {
            TransactionStatus.CREATED when transaction.AcceptDeadline.HasValue
                => ("accept", transaction.AcceptDeadline.Value),
            TransactionStatus.ACCEPTED when transaction.SellerConfirmDeadline.HasValue
                => ("seller_confirm", transaction.SellerConfirmDeadline.Value),
            TransactionStatus.SELLER_CONFIRMED when transaction.PaymentDeadline.HasValue
                => ("payment", transaction.PaymentDeadline.Value),
            TransactionStatus.PAYMENT_RECEIVED when transaction.DeliveryDeadline.HasValue
                => ("delivery", transaction.DeliveryDeadline.Value),

            // ITEM_DELIVERED carries the settlement countdown instead of a
            // timeout: nothing gets cancelled when it elapses, the payout
            // simply becomes eligible for its final check (02 §4.5.1).
            TransactionStatus.ITEM_DELIVERED when transaction.PayoutEligibleAt.HasValue
                => ("settlement", transaction.PayoutEligibleAt.Value),

            _ => (string.Empty, default(DateTime)),
        };

        if (string.IsNullOrEmpty(type)) return null;

        var frozen = transaction.TimeoutFrozenAt.HasValue;
        var remaining = frozen
            ? transaction.TimeoutRemainingSeconds ?? 0
            : Math.Max(0, (int)Math.Floor((expiresAt - nowUtc).TotalSeconds));

        return new TransactionTimeoutDto(
            Type: type,
            ExpiresAt: expiresAt,
            RemainingSeconds: remaining,
            WarningThresholdPercent: warningPercent,
            Frozen: frozen,
            FrozenReason: transaction.TimeoutFreezeReason?.ToString(),
            FrozenAt: transaction.TimeoutFrozenAt);
    }

    private static AvailableActionsDto BuildAuthenticatedActions(
        Transaction transaction,
        string role,
        DateTime nowUtc)
    {
        // EMERGENCY_HOLD freeze — every action becomes false (07 §7.5).
        if (transaction.IsOnHold)
        {
            return new AvailableActionsDto(
                CanAccept: false,
                CanConfirmReady: false,
                CanConfirmReceipt: false,
                CanCancel: false,
                CanDispute: false,
                DisputableTypes: Array.Empty<DisputeType>(),
                CanEscalate: false,
                RequiresLogin: null);
        }

        // 07 §7.5 / 03 §3.2 — canAccept = eligible buyer + still CREATED. role
        // is only ever "buyer" for an eligible viewer (the named BuyerId, or the
        // STEAM_ID-target pre-acceptance match — see role resolution above), so
        // it already encodes the "Steam ID eşleşiyor" gate. A registered STEAM_ID
        // buyer has BuyerId set at create time (TransactionCreationService);
        // gating on BuyerId-is-null wrongly disabled their Accept button even
        // though the accept endpoint (party guard) admits them. The IsOnHold
        // early-return above keeps held transactions at all-false.
        var canAccept = role == "buyer"
            && transaction.Status == TransactionStatus.CREATED;

        // 02 §7 — cancel rights are asymmetric once money is in escrow. The
        // buyer loses the right the moment they pay; the seller keeps it,
        // because they may still decide not to send. Closing the seller's route
        // would not protect the buyer — the seller would simply sit out the
        // deadline and the buyer would wait longer for the same refund.
        var canCancel = role switch
        {
            "buyer" => _activeStatesForBuyerCancel.Contains(transaction.Status)
                       && transaction.PaymentReceivedAt is null,
            "seller" => _activeStatesForBuyerCancel.Contains(transaction.Status)
                        || transaction.Status == TransactionStatus.PAYMENT_RECEIVED,
            _ => false,
        };

        // 03 §2.3 — the seller confirms readiness before the deposit address is
        // revealed to the buyer.
        var canConfirmReady = role == "seller"
            && transaction.Status == TransactionStatus.ACCEPTED;

        // 03 §3.5 — the buyer's own confirmation is sufficient delivery proof.
        var canConfirmReceipt = role == "buyer"
            && transaction.Status == TransactionStatus.PAYMENT_RECEIVED;

        // WP5 (T58-canDisputeEnvelopeBit) — per-type eligibility from the shared
        // matrix; CanDispute stays as the any-type convenience bit. The duplicate
        // -type guard (already-opened types) is enforced by the open endpoint.
        var disputableTypes = role == "buyer" && !transaction.HasActiveDispute
            ? DisputeEligibility.DisputableTypesFor(transaction.Status)
            : Array.Empty<DisputeType>();
        var canDispute = disputableTypes.Count > 0;

        // canEscalate only meaningful once a dispute exists and auto-check
        // completed; T58 is the producer of this signal — false until then.
        var canEscalate = false;

        return new AvailableActionsDto(
            CanAccept: canAccept,
            CanConfirmReady: canConfirmReady,
            CanConfirmReceipt: canConfirmReceipt,
            CanCancel: canCancel,
            CanDispute: canDispute,
            DisputableTypes: disputableTypes,
            CanEscalate: canEscalate,
            RequiresLogin: null);
    }

    /// <summary>
    /// 07 §7.1/§7.5 status projection. EMERGENCY_HOLD is not a
    /// <see cref="TransactionStatus"/> value — it is the overlay surfaced when
    /// <c>IsOnHold=true</c> sits on top of any active state (06 §2.20, 04 §7.3).
    /// Mirrors <c>TransactionListService.ProjectStatus</c> so the detail surface
    /// (hold banner + frozen action panel, keyed off status=="EMERGENCY_HOLD")
    /// fires per 04 §7.3. Applied on both the authenticated and public paths.
    /// </summary>
    private static string ProjectStatus(Transaction transaction) =>
        transaction.IsOnHold ? "EMERGENCY_HOLD" : transaction.Status.ToString();

    private static bool IsTerminal(TransactionStatus status) =>
        status == TransactionStatus.COMPLETED
        || status == TransactionStatus.CANCELLED_TIMEOUT
        || status == TransactionStatus.CANCELLED_SELLER
        || status == TransactionStatus.CANCELLED_BUYER
        || status == TransactionStatus.CANCELLED_ADMIN;

    private async Task<SellerPayoutDto?> BuildSellerPayoutAsync(
        Transaction transaction,
        string? role,
        CancellationToken cancellationToken)
    {
        // 07 §7.5 — the seller payout breakdown is surfaced only in the
        // seller's view of a COMPLETED transaction.
        if (role != "seller" || transaction.Status != TransactionStatus.COMPLETED)
            return null;

        var payout = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .Where(b => b.TransactionId == transaction.Id
                && b.Type == BlockchainTransactionType.SELLER_PAYOUT)
            .OrderByDescending(b => b.ConfirmedAt ?? b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (payout is null) return null;

        var split = FinancialCalculator.ReconstructSellerPayoutSplit(
            transaction.Price, payout.Amount, payout.GasFee);

        return new SellerPayoutDto(
            GrossAmount: FormatMoney(split.GrossAmount),
            GasFee: FormatMoney(split.TotalGasFee),
            GasFeeFromCommission: FormatMoney(split.GasFeeFromCommission),
            GasFeeFromSeller: FormatMoney(split.GasFeeFromSeller),
            NetAmount: FormatMoney(split.NetAmount),
            WalletAddress: payout.ToAddress,
            TxHash: payout.TxHash ?? string.Empty,
            SentAt: payout.ConfirmedAt ?? payout.CreatedAt);
    }

    private async Task<RefundDto?> BuildRefundAsync(
        Transaction transaction,
        string? role,
        CancellationToken cancellationToken)
    {
        // WP2 / 07 §7.5 — the refund breakdown is surfaced in the buyer's view
        // once a payment refund (BUYER_REFUND) has been queued for a cancelled
        // transaction. Reconstructed from the row exactly as the admin detail
        // does (AdminTransactionQueryService.BuildRefundDetail): the gas
        // estimate was snapshotted onto GasFee, so originalAmount = Amount +
        // GasFee = TotalAmount (02 §4.6). TxHash / RefundedAt stay null until
        // the dispatcher broadcasts and the confirmation job finalises on chain.
        if (role != "buyer")
            return null;

        var refund = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .Where(b => b.TransactionId == transaction.Id
                && b.Type == BlockchainTransactionType.BUYER_REFUND)
            .OrderByDescending(b => b.ConfirmedAt ?? b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (refund is null) return null;

        var gasFee = refund.GasFee ?? 0m;
        return new RefundDto(
            OriginalAmount: FormatMoney(refund.Amount + gasFee),
            GasFee: FormatMoney(gasFee),
            NetRefundAmount: FormatMoney(refund.Amount),
            RefundAddress: refund.ToAddress,
            TxHash: refund.TxHash,
            RefundedAt: refund.ConfirmedAt);
    }

    private static decimal? ComputeReputation(decimal? successRate)
    {
        // 06 §3.1 / T43 closure: composite score is
        // ROUND(SuccessfulTransactionRate × 5, 1, ToZero). Threshold gating
        // (account age + completed-tx count) is enforced by T43's read path
        // wherever the score is exposed publicly; for the detail endpoint we
        // fall back to null when no rate is denormalized yet, mirroring the
        // T33 user-profile contract (rate=null ⇒ reputationScore=null).
        if (!successRate.HasValue) return null;
        return Math.Round(successRate.Value * 5m, 1, MidpointRounding.ToZero);
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
