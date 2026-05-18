namespace Skinora.Notifications.Application.Webhooks;

/// <summary>
/// Resend webhook event types we explicitly handle (T78 — 08 §4.3).
/// Mirrors Resend's <c>type</c> field with the <c>email.</c> prefix
/// stripped; any event not enumerated here is acknowledged but
/// otherwise ignored (forward-compat with Resend's roadmap).
/// </summary>
public enum ResendWebhookEventType
{
    /// <summary>email.bounced — recipient mailbox unreachable. Disables EMAIL channel.</summary>
    Bounced,

    /// <summary>email.delivery_delayed — temporary delay. Log + monitor only.</summary>
    DeliveryDelayed,

    /// <summary>email.complained — spam complaint. Disables EMAIL channel + admin alert.</summary>
    Complained,

    /// <summary>email.failed — terminal Resend-side failure. Admin alert.</summary>
    Failed,

    /// <summary>email.suppressed — address on Resend suppression list. Disables EMAIL channel.</summary>
    Suppressed,

    /// <summary>Anything Resend may add in the future — acknowledged + logged.</summary>
    Unknown,
}
