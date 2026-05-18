using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Email;

/// <summary>
/// <see cref="IResendEmailClient"/> backed by a plain <see cref="HttpClient"/>.
/// Plan T78 deliberately rules the community <c>Resend</c> NuGet (pre-1.0,
/// single-endpoint coverage) out — a hand-rolled wrapper keeps the
/// dependency surface minimal and lets us inject an
/// <see cref="HttpMessageHandler"/> mock in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Error mapping per 08 §4.3:
/// </para>
/// <list type="bullet">
///   <item>200 → <see cref="ResendSendEmailResult"/> (returns Resend's
///         message id for webhook correlation).</item>
///   <item>422 / 401 / 403 / other 4xx → <see cref="ResendPermanentException"/>
///         — caller flips the delivery row straight to <c>FAILED</c> +
///         admin alert.</item>
///   <item>429 / 5xx / transport (timeout, network) →
///         <see cref="ResendTransientException"/> — caller retries on the
///         immediate-tier backoff and then escalates to the deferred-tier
///         job.</item>
/// </list>
/// </remarks>
public sealed class ResendEmailClient : IResendEmailClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ResendSettings _settings;
    private readonly ILogger<ResendEmailClient> _logger;

    public ResendEmailClient(
        HttpClient httpClient,
        IOptions<ResendSettings> settings,
        ILogger<ResendEmailClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException(
                "Resend API key is not configured (Resend:ApiKey). " +
                "Set Resend:Provider to 'logging' for non-production environments.");
        }

        if (string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            throw new InvalidOperationException("Resend From address is not configured (Resend:FromAddress).");
        }

        // The Bearer token is set on the shared HttpClient at registration
        // time; the DI extension passes the same IOptions<ResendSettings>
        // value into the configure delegate. Belt-and-braces: enforce here
        // in case a caller constructs the client manually.
        _httpClient.DefaultRequestHeaders.Authorization ??=
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        }
    }

    public async Task<ResendSendEmailResult> SendAsync(
        ResendSendEmailRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new SendEmailPayload
        {
            From = _settings.FromAddress,
            To = new[] { request.ToAddress },
            Subject = request.Subject,
            Html = request.HtmlBody,
            Text = request.TextBody,
            Tags = request.Tags?.Select(kv => new TagPayload { Name = kv.Key, Value = kv.Value }).ToArray(),
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("emails", payload, JsonOptions, cancellationToken);
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — Resend was unreachable within the configured budget.
            // Classified transient so the immediate retry tier can re-try.
            throw new ResendTransientException(
                "Resend request timed out before a response was received.",
                innerException: tex);
        }
        catch (HttpRequestException hex)
        {
            throw new ResendTransientException(
                $"Resend HTTP transport error: {hex.Message}",
                innerException: hex);
        }

        if (response.IsSuccessStatusCode)
        {
            ResendSuccessBody? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<ResendSuccessBody>(JsonOptions, cancellationToken);
            }
            catch (JsonException jex)
            {
                // 200 with malformed body — treat as transient so a retry
                // can confirm. Should never happen with the documented API
                // but the wrapper must not deserialize-throw to the caller.
                throw new ResendTransientException(
                    "Resend returned 200 but the response body could not be parsed.",
                    httpStatusCode: (int)response.StatusCode,
                    innerException: jex);
            }

            if (body is null || string.IsNullOrEmpty(body.Id))
            {
                throw new ResendTransientException(
                    "Resend returned 200 without an email id.",
                    httpStatusCode: (int)response.StatusCode);
            }

            _logger.LogDebug(
                "Resend send accepted — messageId={MessageId} status={Status}",
                body.Id,
                (int)response.StatusCode);

            return new ResendSendEmailResult(body.Id);
        }

        // Failure — read body for diagnostic context (best-effort).
        ResendErrorBody? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ResendErrorBody>(JsonOptions, cancellationToken);
        }
        catch
        {
            // Resend always returns JSON on documented errors but we don't
            // want a body-parse failure to mask the HTTP status itself.
        }

        var statusCode = (int)response.StatusCode;
        var errorName = error?.Name;
        var errorMessage = error?.Message
                           ?? $"Resend returned HTTP {statusCode} ({response.ReasonPhrase}).";

        // Transient set per 08 §4.3.
        if (statusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "Resend transient failure — status={Status} name={Name} message={Message}",
                statusCode,
                errorName,
                errorMessage);

            throw new ResendTransientException(errorMessage, statusCode, errorName);
        }

        // Everything else: 4xx (422 validation, 401 auth, 403 forbidden,
        // 404 endpoint mismatch, …) — permanent, no retry.
        _logger.LogWarning(
            "Resend permanent failure — status={Status} name={Name} message={Message}",
            statusCode,
            errorName,
            errorMessage);

        throw new ResendPermanentException(errorMessage, statusCode, errorName);
    }

    private sealed class SendEmailPayload
    {
        public string From { get; set; } = string.Empty;
        public string[] To { get; set; } = Array.Empty<string>();
        public string Subject { get; set; } = string.Empty;
        public string? Html { get; set; }
        public string? Text { get; set; }
        public TagPayload[]? Tags { get; set; }
    }

    private sealed class TagPayload
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ResendSuccessBody
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class ResendErrorBody
    {
        public int? StatusCode { get; set; }
        public string? Name { get; set; }
        public string? Message { get; set; }
    }
}
