namespace Skinora.Shared.Enums;

/// <summary>
/// Transaction state machine triggers (05 §4.2 transition table).
/// </summary>
public enum TransactionTrigger
{
    BuyerAccept,
    SendTradeOfferToSeller,
    EscrowItem,
    ConfirmPayment,
    SendTradeOfferToBuyer,
    DeliverItem,
    Complete,
    Timeout,
    SellerCancel,
    BuyerCancel,
    AdminCancel,
    SellerDecline,
    BuyerDecline,
    AdminApprove,
    AdminReject,

    // WP5 / T58 — buyer-favor admin dispute resolution. Permitted from the
    // disputed states (ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER,
    // ITEM_DELIVERED) into the terminal REFUNDED state.
    AdminResolveRefund
}
