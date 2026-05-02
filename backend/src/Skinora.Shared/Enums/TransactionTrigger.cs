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
    AdminReject
}
