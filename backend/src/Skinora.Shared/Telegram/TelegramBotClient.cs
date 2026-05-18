using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Telegram;

/// <summary>
/// <see cref="ITelegramBotClient"/> backed by a plain
/// <see cref="HttpClient"/>. Plan T79 deliberately rules the community
/// <c>Telegram.Bot</c> NuGet out — a hand-rolled wrapper keeps the
/// dependency surface minimal, matches the
/// <see cref="Shared.Email.ResendEmailClient"/> precedent and lets us
/// inject a mock <see cref="HttpMessageHandler"/> in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Error mapping per 08 §5.4:
/// </para>
/// <list type="bullet">
///   <item>200 with <c>ok=true</c> → <see cref="TelegramSendMessageResult"/>.</item>
///   <item>429 → <see cref="TelegramTransientException"/> populated with
///         <c>retry_after</c> for the caller's backoff.</item>
///   <item>5xx / transport (timeout, network) →
///         <see cref="TelegramTransientException"/>.</item>
///   <item>403 → <see cref="TelegramForbiddenException"/> with parsed
///         <see cref="TelegramForbiddenReason"/>.</item>
///   <item>Any other 4xx (400, 401, …) →
///         <see cref="TelegramPermanentException"/>.</item>
/// </list>
/// </remarks>
public sealed class TelegramBotClient : ITelegramBotClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramBotClient> _logger;

    public TelegramBotClient(
        HttpClient httpClient,
        IOptions<TelegramSettings> settings,
        ILogger<TelegramBotClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            throw new InvalidOperationException(
                "Telegram bot token is not configured (Telegram:BotToken). " +
                "Set Telegram:Provider to 'logging' for non-production environments.");
        }

        if (_httpClient.BaseAddress is null)
        {
            // The Telegram Bot API embeds the token in the URL path; both
            // sendMessage and setWebhook live under
            // /bot{token}/{method}, so we lock the BaseAddress to the
            // bot's namespace and let callers POST against the relative
            // method name.
            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            _httpClient.BaseAddress = new Uri(
                $"{baseUrl}/bot{_settings.BotToken}/",
                UriKind.Absolute);
        }
    }

    public Task<TelegramSendMessageResult> SendMessageAsync(
        TelegramSendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new SendMessagePayload
        {
            ChatId = request.ChatId,
            Text = request.Text,
            ParseMode = "MarkdownV2",
            DisableNotification = request.DisableNotification ? true : null,
        };

        return PostAsync<SendMessagePayload, SendMessageResponse, TelegramSendMessageResult>(
            "sendMessage",
            payload,
            result => new TelegramSendMessageResult(result.MessageId),
            cancellationToken);
    }

    public async Task SetWebhookAsync(
        TelegramSetWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new SetWebhookPayload
        {
            Url = request.Url,
            SecretToken = request.SecretToken,
            MaxConnections = request.MaxConnections,
            AllowedUpdates = (request.AllowedUpdates ?? new[] { "message" }).ToArray(),
            DropPendingUpdates = request.DropPendingUpdates ? true : null,
        };

        await PostAsync<SetWebhookPayload, bool, bool>(
            "setWebhook",
            payload,
            result => result,
            cancellationToken);
    }

    private async Task<TOut> PostAsync<TPayload, TResult, TOut>(
        string method,
        TPayload payload,
        Func<TResult, TOut> projection,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(method, payload, JsonOptions, cancellationToken);
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TelegramTransientException(
                $"Telegram {method} timed out before a response was received.",
                innerException: tex);
        }
        catch (HttpRequestException hex)
        {
            throw new TelegramTransientException(
                $"Telegram {method} HTTP transport error: {hex.Message}",
                innerException: hex);
        }

        TelegramEnvelope<TResult>? envelope = null;
        try
        {
            envelope = await response.Content
                .ReadFromJsonAsync<TelegramEnvelope<TResult>>(JsonOptions, cancellationToken);
        }
        catch (JsonException jex)
        {
            // Telegram always responds with JSON, even on failure. Body
            // parse failure means a transient infra event (gateway HTML
            // page, partial response). Bubble up as transient so the
            // immediate retry tier can confirm.
            throw new TelegramTransientException(
                $"Telegram {method} returned HTTP {(int)response.StatusCode} but the body could not be parsed.",
                httpStatusCode: (int)response.StatusCode,
                innerException: jex);
        }

        if (response.IsSuccessStatusCode && envelope is { Ok: true, Result: not null })
        {
            return projection(envelope.Result);
        }

        var statusCode = (int)response.StatusCode;
        var description = envelope?.Description ?? response.ReasonPhrase ?? string.Empty;
        var errorCode = envelope?.ErrorCode;
        var retryAfter = envelope?.Parameters?.RetryAfter;

        if (statusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "Telegram {Method} transient failure — status={Status} retry_after={RetryAfter} description={Description}",
                method,
                statusCode,
                retryAfter,
                description);

            throw new TelegramTransientException(
                description,
                httpStatusCode: statusCode,
                telegramErrorCode: errorCode,
                telegramErrorDescription: description,
                retryAfterSeconds: retryAfter);
        }

        if (statusCode == 403)
        {
            var reason = ClassifyForbidden(description);

            _logger.LogWarning(
                "Telegram {Method} forbidden — reason={Reason} description={Description}",
                method,
                reason,
                description);

            throw new TelegramForbiddenException(
                reason,
                description,
                telegramErrorCode: errorCode,
                telegramErrorDescription: description);
        }

        _logger.LogWarning(
            "Telegram {Method} permanent failure — status={Status} code={Code} description={Description}",
            method,
            statusCode,
            errorCode,
            description);

        throw new TelegramPermanentException(
            description,
            httpStatusCode: statusCode,
            telegramErrorCode: errorCode,
            telegramErrorDescription: description);
    }

    internal static TelegramForbiddenReason ClassifyForbidden(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return TelegramForbiddenReason.Unknown;
        }

        // 08 §5.4 — exact-suffix match against Telegram's documented
        // error_description strings. Telegram occasionally appends extra
        // context after the documented phrase, so we Contains rather
        // than Equals.
        var d = description.Trim();
        if (d.Contains("bot was blocked by the user", StringComparison.OrdinalIgnoreCase))
            return TelegramForbiddenReason.BotBlockedByUser;
        if (d.Contains("user is deactivated", StringComparison.OrdinalIgnoreCase))
            return TelegramForbiddenReason.UserDeactivated;
        if (d.Contains("bot can't send messages to bots", StringComparison.OrdinalIgnoreCase))
            return TelegramForbiddenReason.CannotMessageBots;
        if (d.Contains("bot can't initiate conversation", StringComparison.OrdinalIgnoreCase))
            return TelegramForbiddenReason.CannotInitiateConversation;

        return TelegramForbiddenReason.Unknown;
    }

    private sealed class SendMessagePayload
    {
        [JsonPropertyName("chat_id")]
        public string ChatId { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("parse_mode")]
        public string ParseMode { get; set; } = "MarkdownV2";

        [JsonPropertyName("disable_notification")]
        public bool? DisableNotification { get; set; }
    }

    private sealed class SetWebhookPayload
    {
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("secret_token")]
        public string SecretToken { get; set; } = string.Empty;

        [JsonPropertyName("max_connections")]
        public int MaxConnections { get; set; } = 40;

        [JsonPropertyName("allowed_updates")]
        public string[] AllowedUpdates { get; set; } = Array.Empty<string>();

        [JsonPropertyName("drop_pending_updates")]
        public bool? DropPendingUpdates { get; set; }
    }

    private sealed class TelegramEnvelope<TResult>
    {
        public bool Ok { get; set; }

        public TResult? Result { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        public string? Description { get; set; }

        public TelegramResponseParameters? Parameters { get; set; }
    }

    private sealed class TelegramResponseParameters
    {
        [JsonPropertyName("retry_after")]
        public int? RetryAfter { get; set; }

        [JsonPropertyName("migrate_to_chat_id")]
        public long? MigrateToChatId { get; set; }
    }

    private sealed class SendMessageResponse
    {
        [JsonPropertyName("message_id")]
        public long MessageId { get; set; }
    }

}
