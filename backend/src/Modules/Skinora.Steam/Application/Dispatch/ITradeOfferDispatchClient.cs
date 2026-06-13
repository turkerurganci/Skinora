using System.Text.Json.Serialization;

namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// Backend → Steam sidecar trade-offer dispatch port (T106a — formalises the
/// T69-K1 dispatch caller). Calls <c>POST /api/trade-offers/send</c> on the
/// sidecar (08 §2.7); the synchronous response reports whether the offer
/// reached Steam. Authoritative lifecycle (accepted/declined/expired) still
/// arrives asynchronously via the <c>trade_offer.*</c> webhooks.
/// </summary>
public interface ITradeOfferDispatchClient
{
    Task<TradeOfferDispatchResult> SendAsync(
        TradeOfferDispatchRequest request, CancellationToken cancellationToken);
}

/// <summary>Direction string sent to the sidecar (matches its <c>TradeDirection</c>).</summary>
public static class TradeOfferDispatchDirection
{
    public const string SellerToBot = "SELLER_TO_BOT";
    public const string BotToBuyer = "BOT_TO_BUYER";
    public const string BotToSellerRefund = "BOT_TO_SELLER_REFUND";
}

/// <summary>
/// Single item descriptor in a dispatch request. Property names are pinned to
/// the sidecar's lowercase <c>ItemDescriptor</c> shape (<c>assetid</c> /
/// <c>appid</c> / <c>contextid</c>) so camelCase web defaults cannot drift them.
/// </summary>
public sealed record TradeOfferDispatchItem(
    [property: JsonPropertyName("assetid")] string AssetId,
    [property: JsonPropertyName("appid")] int AppId,
    [property: JsonPropertyName("contextid")] string ContextId);

/// <summary>
/// Request body for <c>POST /api/trade-offers/send</c>. Serialised as the
/// sidecar's <c>SendTradeOfferRequest</c>.
/// </summary>
public sealed record TradeOfferDispatchRequest(
    [property: JsonPropertyName("transactionId")] Guid TransactionId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("partnerSteamId")] string PartnerSteamId,
    [property: JsonPropertyName("items")] IReadOnlyList<TradeOfferDispatchItem> Items,
    [property: JsonPropertyName("botAccountName")] string? BotAccountName,
    [property: JsonPropertyName("message")] string? Message = null);

/// <summary>Outcome of a dispatch attempt.</summary>
public enum TradeOfferDispatchStatus
{
    /// <summary>Offer reached Steam (sidecar status sent/confirmed).</summary>
    Sent,

    /// <summary>Offer created on Steam but mobile confirmation outstanding (status pending).</summary>
    Pending,

    /// <summary>
    /// Sidecar processed the request but the offer could not be sent (retries
    /// exhausted, permanent eResult, item rejected). Terminal for the caller.
    /// </summary>
    Failed,

    /// <summary>
    /// The sidecar was unreachable or returned 5xx/503 / the call timed out.
    /// Transient — the caller leaves the transaction for the next scan tick.
    /// </summary>
    Unavailable,
}

/// <summary>Discriminated dispatch result. <see cref="OfferId"/> is set on success.</summary>
public sealed record TradeOfferDispatchResult(
    TradeOfferDispatchStatus Status,
    string? OfferId,
    bool Retryable,
    int Attempts,
    string? Reason);
