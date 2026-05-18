namespace Skinora.Shared.Email;

/// <summary>
/// Outbound payload for Resend <c>POST /emails</c> (08 §4.2). The
/// transport (<see cref="IResendEmailClient"/>) supplies the
/// <c>From</c> address from <see cref="ResendSettings.FromAddress"/>
/// so callers never have to know the verified-domain mailbox.
/// </summary>
public sealed record ResendSendEmailRequest(
    string ToAddress,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    IReadOnlyDictionary<string, string>? Tags = null);

/// <summary>
/// Successful response — Resend returns the message id which we persist
/// on <see cref="Skinora.Notifications.Domain.Entities"/> rows for
/// support traceability and as a webhook-correlation key (08 §4.3).
/// </summary>
public sealed record ResendSendEmailResult(string MessageId);
