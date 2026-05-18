namespace Skinora.Notifications.Application.Webhooks;

/// <summary>
/// Outcome of a Resend webhook callback (T78 — mirrors
/// <c>BlockchainWebhookResult</c> for symmetry across sidecar bus
/// shapes). The controller serialises the value as the response body
/// so the Resend dashboard can show "Acknowledged" / "Ignored".
/// </summary>
public enum ResendWebhookResult
{
    /// <summary>Action taken (channel disabled, alert raised, etc.).</summary>
    Applied,

    /// <summary>Event already processed (svix-id duplicate). No-op.</summary>
    Idempotent,

    /// <summary>Recognised event but recipient not found in our DB. No-op.</summary>
    UnknownRecipient,

    /// <summary>Logged only — informational events (delivery_delayed, forward-compat unknown types).</summary>
    Acknowledged,
}
