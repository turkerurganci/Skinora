namespace Skinora.Shared.Enums;

/// <summary>
/// Identifies why an <c>ItemRefundToSellerRequestedEvent</c> fired so the Steam
/// sidecar consumer (T64–T68) can correlate the return-trade-offer back to the
/// originating lifecycle path. The action is identical for every trigger
/// (return the escrowed item to the seller); the value is informational.
/// </summary>
/// <remarks>
/// Replaces the previous <c>TimeoutPhase</c> typing of the event's <c>Trigger</c>
/// field — that enum is timeout-only, but item-refund requests now also originate
/// from user-initiated cancellation (T51) and admin-initiated cancellation (T59,
/// forward-deferred).
/// </remarks>
public enum ItemRefundTrigger
{
    /// <summary>Payment timeout fired in <c>ITEM_ESCROWED</c> (T49 — 03 §4.3).</summary>
    TimeoutPayment,

    /// <summary>Delivery timeout fired in <c>TRADE_OFFER_SENT_TO_BUYER</c> (T49 — 03 §4.4).</summary>
    TimeoutDelivery,

    /// <summary>Seller-initiated cancellation while the item was on the platform (T51 — 02 §7, 03 §2.5).</summary>
    SellerCancel,

    /// <summary>Buyer-initiated cancellation while the item was on the platform (T51 — 02 §7, 03 §3.3).</summary>
    BuyerCancel,
}
