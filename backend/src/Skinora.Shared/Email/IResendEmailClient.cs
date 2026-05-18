namespace Skinora.Shared.Email;

/// <summary>
/// Low-level HTTP transport for Resend's <c>POST /emails</c> endpoint
/// (08 §4.2). Independent of who is calling it — both the notification
/// dispatcher (notifications module) and the email verification service
/// (users module) consume this interface so authentication, retry
/// classification and Resend error mapping live in one place.
/// </summary>
/// <remarks>
/// <para>
/// Throws either <see cref="ResendTransientException"/> (5xx, 429,
/// network/transport) or <see cref="ResendPermanentException"/>
/// (4xx — validation, auth, forbidden). Successful calls return the
/// <see cref="ResendSendEmailResult.MessageId"/> for downstream
/// correlation with webhook events.
/// </para>
/// </remarks>
public interface IResendEmailClient
{
    Task<ResendSendEmailResult> SendAsync(
        ResendSendEmailRequest request,
        CancellationToken cancellationToken);
}
