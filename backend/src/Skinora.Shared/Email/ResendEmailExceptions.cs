namespace Skinora.Shared.Email;

/// <summary>
/// Root exception type for Resend transport failures. Callers should
/// never catch this directly — branch on the
/// <see cref="ResendTransientException"/> /
/// <see cref="ResendPermanentException"/> subclasses so retry decisions
/// stay correct (08 §4.3 — 5xx/429 retryable, 4xx/422/401 not).
/// </summary>
public abstract class ResendEmailException : Exception
{
    public int? HttpStatusCode { get; }
    public string? ResendErrorName { get; }

    protected ResendEmailException(
        string message,
        int? httpStatusCode,
        string? resendErrorName,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        ResendErrorName = resendErrorName;
    }
}

/// <summary>
/// Transient failure — Resend returned 5xx, 429, or the call failed at
/// the transport layer (network, DNS, timeout). The notification
/// delivery pipeline (NotificationDeliveryJob) retries with the
/// immediate-tier backoff (1 dk / 5 dk / 15 dk); once the budget is
/// exhausted the row flips to <c>DEFERRED</c> and the deferred-tier
/// job (30 dk / 1 sa / 4 sa) picks it up.
/// </summary>
public sealed class ResendTransientException : ResendEmailException
{
    public ResendTransientException(
        string message,
        int? httpStatusCode = null,
        string? resendErrorName = null,
        Exception? innerException = null)
        : base(message, httpStatusCode, resendErrorName, innerException)
    {
    }
}

/// <summary>
/// Permanent failure — Resend returned 422 (validation), 401 (api key
/// invalid), 403 (forbidden), or any other 4xx that retrying cannot
/// resolve. The delivery row is flipped straight to <c>FAILED</c> with
/// an admin alert (no retry, no DEFERRED).
/// </summary>
public sealed class ResendPermanentException : ResendEmailException
{
    public ResendPermanentException(
        string message,
        int? httpStatusCode = null,
        string? resendErrorName = null,
        Exception? innerException = null)
        : base(message, httpStatusCode, resendErrorName, innerException)
    {
    }
}
