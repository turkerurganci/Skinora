namespace Skinora.Notifications.Application.Webhooks;

/// <summary>
/// Inbound Resend webhook dispatcher (T78 — 08 §4.3). The controller
/// resolves Svix signature + idempotency upstream; this handler is
/// invoked only with an authenticated, deduplicated event.
/// </summary>
public interface IResendWebhookHandler
{
    Task<ResendWebhookResult> HandleAsync(
        ResendWebhookEnvelope envelope,
        string svixId,
        CancellationToken cancellationToken);
}
