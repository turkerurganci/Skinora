using System.Text.Json.Serialization;

namespace Skinora.Notifications.Application.Webhooks;

/// <summary>
/// JSON envelope shape that Resend posts to <c>/api/v1/webhooks/resend</c>
/// (T78 — 08 §4.3). The Svix wrapper adds <c>type</c>, <c>created_at</c>
/// and <c>data</c> fields around the email-specific payload.
/// </summary>
/// <remarks>
/// Resend documents the <c>data</c> object for email events as carrying
/// <c>email_id</c>, <c>from</c>, <c>to</c>, <c>subject</c> and an
/// optional <c>tags</c> map. We surface only the fields we act on so
/// new fields cannot break deserialization.
/// </remarks>
public sealed class ResendWebhookEnvelope
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("data")]
    public ResendWebhookEventData? Data { get; set; }
}

public sealed class ResendWebhookEventData
{
    [JsonPropertyName("email_id")]
    public string? EmailId { get; set; }

    [JsonPropertyName("to")]
    public string[]? To { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
}
