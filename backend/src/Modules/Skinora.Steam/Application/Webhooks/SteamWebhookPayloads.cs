using System.Text.Json.Serialization;

namespace Skinora.Steam.Application.Webhooks;

/// <summary>
/// Inbound webhook envelopes from the Steam sidecar (09 §17.5,
/// <c>sidecar-steam/src/webhook/WebhookPayloads.ts</c>). The signature
/// envelope (timestamp / nonce / X-Signature) is handled by
/// <c>WebhookSignatureMiddleware</c> upstream — controllers see only the
/// JSON body.
///
/// <para>
/// The sidecar groups events into two transports:
/// </para>
/// <list type="bullet">
///   <item><c>POST /api/v1/webhooks/steam/bot-events</c> — bot lifecycle</item>
///   <item><c>POST /api/v1/webhooks/steam/trade-events</c> — trade offer</item>
/// </list>
/// </summary>
public static class SteamWebhookEvents
{
    // Bot lifecycle (T64 publisher).
    public const string BotSessionFailed = "bot.session_failed";
    public const string BotRemovedFromPool = "bot.removed_from_pool";

    // Trade offer (T65 + T66 publisher).
    public const string TradeOfferSent = "trade_offer.sent";
    public const string TradeOfferFailed = "trade_offer.failed";
    public const string TradeOfferAccepted = "trade_offer.accepted";
    public const string TradeOfferDeclined = "trade_offer.declined";
    public const string TradeOfferExpired = "trade_offer.expired";
    public const string TradeOfferCountered = "trade_offer.countered";
    public const string TradeOfferInvalidItems = "trade_offer.invalid_items";
}

/// <summary>
/// Outer envelope shared by every sidecar webhook.
/// </summary>
public sealed class SteamWebhookEnvelope<TData>
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public TData Data { get; set; } = default!;
}

/// <summary>Bot-events shaped data. Discriminated by <c>Event</c>.</summary>
public sealed class BotEventData
{
    [JsonPropertyName("accountName")]
    public string? AccountName { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Common fields for every trade event. Direction-specific routing uses
/// <see cref="Direction"/> ("escrow" → seller leg, "delivery" → buyer leg).
/// </summary>
public sealed class TradeOfferEventData
{
    [JsonPropertyName("transactionId")]
    public Guid? TransactionId { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("partnerSteamId")]
    public string? PartnerSteamId { get; set; }

    [JsonPropertyName("botSteamId")]
    public string? BotSteamId { get; set; }

    [JsonPropertyName("botAccountName")]
    public string? BotAccountName { get; set; }

    [JsonPropertyName("offerId")]
    public string? OfferId { get; set; }

    /// <summary>
    /// <c>trade_offer.sent</c> only: "pending" | "sent" | "confirmed".
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// <c>trade_offer.failed</c>: reason string sent by sidecar after retries
    /// are exhausted or a permanent eResult is hit.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("attempts")]
    public int? Attempts { get; set; }

    [JsonPropertyName("eresult")]
    public int? EResult { get; set; }

    [JsonPropertyName("retryable")]
    public bool? Retryable { get; set; }

    [JsonPropertyName("oldState")]
    public int? OldState { get; set; }

    [JsonPropertyName("newState")]
    public int? NewState { get; set; }
}
