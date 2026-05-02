using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
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
    private const int DefaultTimeoutWarningPercent = 75;

    private static readonly TransactionStatus[] _activeStatesForCancel =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.ITEM_ESCROWED,
    ];

    private static readonly TransactionStatus[] _disputeAllowedStates =
    [
        TransactionStatus.ITEM_ESCROWED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
        TransactionStatus.ITEM_DELIVERED,
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

        if (callerId is null)
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

        var timeout = BuildTimeout(transaction, nowUtc);

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
                ItemReturned: transaction.ItemEscrowedAt.HasValue,
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

        var actions = BuildAuthenticatedActions(transaction, role!, nowUtc);

        var dto = new TransactionDetailDto(
            Id: transaction.Id,
            Status: transaction.Status,
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
            SellerPayout: null,      // T73 payout
            Refund: null,            // T49/T51 refunds
            CancelInfo: cancel,
            FlagInfo: flag,
            HoldInfo: hold,
            Dispute: null,           // T58 dispute
            InviteInfo: invite,
            PaymentEvents: ProducePaymentEventsArray(transaction),
            EscrowBotAssetId: transaction.EscrowBotAssetId,
            DeliveredBuyerAssetId: transaction.DeliveredBuyerAssetId,
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
            Status: transaction.Status,
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
            EscrowBotAssetId: null,
            DeliveredBuyerAssetId: null,
            AvailableActions: new AvailableActionsDto(
                CanAccept: false,
                CanCancel: null,
                CanDispute: null,
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
        // 07 §7.5: paymentEvents available from ITEM_ESCROWED+. Empty array
        // before then would imply "we have data, none to show"; we return
        // null so the field is suppressed entirely until T70+ wires it.
        if (transaction.Status == TransactionStatus.CREATED
            || transaction.Status == TransactionStatus.FLAGGED
            || transaction.Status == TransactionStatus.ACCEPTED
            || transaction.Status == TransactionStatus.TRADE_OFFER_SENT_TO_SELLER)
            return null;
        return Array.Empty<PaymentEventDto>();
    }

    private static TransactionTimeoutDto? BuildTimeout(Transaction transaction, DateTime nowUtc)
    {
        // 07 §7.5 timeout block — surfaced when an active deadline is in
        // play. Terminal states (COMPLETED, CANCELLED_*) hide the block.
        if (IsTerminal(transaction.Status)) return null;

        var (type, expiresAt) = transaction.Status switch
        {
            TransactionStatus.CREATED when transaction.AcceptDeadline.HasValue
                => ("accept", transaction.AcceptDeadline.Value),
            TransactionStatus.TRADE_OFFER_SENT_TO_SELLER when transaction.TradeOfferToSellerDeadline.HasValue
                => ("trade_offer_seller", transaction.TradeOfferToSellerDeadline.Value),
            TransactionStatus.ITEM_ESCROWED when transaction.PaymentDeadline.HasValue
                => ("payment", transaction.PaymentDeadline.Value),
            TransactionStatus.TRADE_OFFER_SENT_TO_BUYER when transaction.TradeOfferToBuyerDeadline.HasValue
                => ("trade_offer_buyer", transaction.TradeOfferToBuyerDeadline.Value),
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
            WarningThresholdPercent: DefaultTimeoutWarningPercent,
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
                CanCancel: false,
                CanDispute: false,
                CanEscalate: false,
                RequiresLogin: null);
        }

        var canAccept = role == "buyer"
            && transaction.Status == TransactionStatus.CREATED
            && transaction.BuyerId is null;

        var canCancel = role is "seller" or "buyer"
            && _activeStatesForCancel.Contains(transaction.Status)
            && transaction.PaymentReceivedAt is null;

        var canDispute = role == "buyer"
            && _disputeAllowedStates.Contains(transaction.Status)
            && !transaction.HasActiveDispute;

        // canEscalate only meaningful once a dispute exists and auto-check
        // completed; T58 is the producer of this signal — false until then.
        var canEscalate = false;

        return new AvailableActionsDto(
            CanAccept: canAccept,
            CanCancel: canCancel,
            CanDispute: canDispute,
            CanEscalate: canEscalate,
            RequiresLogin: null);
    }

    private static bool IsTerminal(TransactionStatus status) =>
        status == TransactionStatus.COMPLETED
        || status == TransactionStatus.CANCELLED_TIMEOUT
        || status == TransactionStatus.CANCELLED_SELLER
        || status == TransactionStatus.CANCELLED_BUYER
        || status == TransactionStatus.CANCELLED_ADMIN;

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
