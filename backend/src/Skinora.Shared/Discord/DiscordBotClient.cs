using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Discord;

/// <summary>
/// <see cref="IDiscordBotClient"/> backed by a plain
/// <see cref="HttpClient"/>. The client is registered as a typed
/// HttpClient (Discord:Provider == "discord" only) so a misconfigured
/// stub-mode build never reaches the network.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="HttpClient.BaseAddress"/> is pinned to
/// <c>BaseUrl/</c>; relative URLs in <see cref="CreateDmAsync"/> and
/// <see cref="SendMessageAsync"/> compose to the absolute v10 routes.
/// </para>
/// <para>
/// Error mapping per 08 §6.4 (Bot API hata tablosu):
/// </para>
/// <list type="bullet">
///   <item>200 / 201 → success.</item>
///   <item>401 → <see cref="DiscordUnauthorizedException"/> (bot token
///         revoked → admin alert, queue pause).</item>
///   <item>403 + createDM → <see cref="DiscordForbiddenException"/>
///         (<see cref="DiscordForbiddenReason.MutualGuildRequired"/>).</item>
///   <item>403 + sendMessage → <see cref="DiscordForbiddenException"/>
///         (<see cref="DiscordForbiddenReason.DmClosed"/> by default;
///         <see cref="DiscordForbiddenReason.Unknown"/> when the
///         Discord error code doesn't match the documented one).</item>
///   <item>404 → <see cref="DiscordPermanentException"/> (channel /
///         user not found — connection broken).</item>
///   <item>5xx + 429 + transport → <see cref="DiscordTransientException"/>
///         with <c>retry_after</c> + bucket captured for the rate
///         limiter handshake.</item>
///   <item>Other 4xx → <see cref="DiscordPermanentException"/>.</item>
/// </list>
/// <para>
/// Rate-limit headers (<c>X-RateLimit-Bucket</c>,
/// <c>X-RateLimit-Reset-After</c>) are surfaced to the caller via
/// <see cref="IDiscordRateLimiter"/> registrations. Discord 429
/// responses carry <c>retry_after</c> as a floating-point seconds
/// value; <see cref="DiscordTransientException"/> preserves the
/// fractional precision so the limiter can wait for the full window.
/// </para>
/// </remarks>
public sealed class DiscordBotClient : IDiscordBotClient
{
    private const string RateLimitBucketHeader = "X-RateLimit-Bucket";
    private const string RateLimitResetAfterHeader = "X-RateLimit-Reset-After";
    private const string RateLimitGlobalHeader = "X-RateLimit-Global";

    // Discord error code 50007 — "Cannot send messages to this user".
    // Surfaced when the user has closed DMs from server members
    // (08 §6.4 row 1). Any other 403 against /messages is reported as
    // Unknown for the admin-alert path.
    private const int CannotSendMessagesToThisUserCode = 50007;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly DiscordSettings _settings;
    private readonly IDiscordRateLimiter _rateLimiter;
    private readonly ILogger<DiscordBotClient> _logger;

    public DiscordBotClient(
        HttpClient httpClient,
        IOptions<DiscordSettings> settings,
        IDiscordRateLimiter rateLimiter,
        ILogger<DiscordBotClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _rateLimiter = rateLimiter;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            throw new InvalidOperationException(
                "Discord bot token is not configured (Discord:BotToken). " +
                "Set Discord:Provider to 'logging' for non-production environments.");
        }

        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            _httpClient.BaseAddress = new Uri(baseUrl + "/", UriKind.Absolute);
        }

        // Discord requires the literal "Bot " token type, distinct from
        // OAuth2 user tokens (which use "Bearer").
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bot", _settings.BotToken);
    }

    public async Task<DiscordDmChannel> CreateDmAsync(
        DiscordCreateDmRequest request, CancellationToken cancellationToken)
    {
        var payload = new CreateDmPayload { RecipientId = request.RecipientId };

        await _rateLimiter.WaitAsync(
            DiscordRateLimitBuckets.CreateDm, cancellationToken);

        return await PostAsync<CreateDmPayload, DmChannelResponse, DiscordDmChannel>(
            "users/@me/channels",
            payload,
            DiscordRateLimitBuckets.CreateDm,
            forbiddenReasonForRoute: DiscordForbiddenReason.MutualGuildRequired,
            projection: body => new DiscordDmChannel(body.Id ?? string.Empty),
            cancellationToken);
    }

    public async Task<DiscordSendMessageResult> SendMessageAsync(
        DiscordSendMessageRequest request, CancellationToken cancellationToken)
    {
        var payload = new SendMessagePayload
        {
            Content = request.Content,
            AllowedMentions = new AllowedMentionsPayload(),
        };

        var bucket = DiscordRateLimitBuckets.SendMessage(request.ChannelId);
        await _rateLimiter.WaitAsync(bucket, cancellationToken);

        return await PostAsync<SendMessagePayload, MessageResponse, DiscordSendMessageResult>(
            $"channels/{request.ChannelId}/messages",
            payload,
            bucket,
            forbiddenReasonForRoute: DiscordForbiddenReason.DmClosed,
            projection: body => new DiscordSendMessageResult(body.Id ?? string.Empty),
            cancellationToken);
    }

    private async Task<TOut> PostAsync<TPayload, TResponse, TOut>(
        string route,
        TPayload payload,
        string bucket,
        DiscordForbiddenReason forbiddenReasonForRoute,
        Func<TResponse, TOut> projection,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(
                route, payload, JsonOptions, cancellationToken);
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DiscordTransientException(
                $"Discord {route} timed out.",
                bucket: bucket,
                innerException: tex);
        }
        catch (HttpRequestException hex)
        {
            throw new DiscordTransientException(
                $"Discord {route} transport failure: {hex.Message}",
                bucket: bucket,
                innerException: hex);
        }

        UpdateRateLimitFromHeaders(response, bucket);

        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            TResponse? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException jex)
            {
                throw new DiscordTransientException(
                    $"Discord {route} returned HTTP {statusCode} but the body could not be parsed.",
                    httpStatusCode: statusCode,
                    bucket: bucket,
                    innerException: jex);
            }

            if (body is null)
            {
                throw new DiscordTransientException(
                    $"Discord {route} returned HTTP {statusCode} with an empty body.",
                    httpStatusCode: statusCode,
                    bucket: bucket);
            }

            return projection(body);
        }

        var error = await TryReadErrorBodyAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = ResolveRetryAfter(response, error);
            var isGlobal = response.Headers.TryGetValues(RateLimitGlobalHeader, out _)
                || (error?.Global ?? false);

            _logger.LogWarning(
                "Discord {Route} rate-limited — bucket={Bucket} retry_after={RetryAfter} global={Global}",
                route,
                bucket,
                retryAfter,
                isGlobal);

            _rateLimiter.RegisterRetryAfter(bucket, retryAfter, isGlobal);

            throw new DiscordTransientException(
                error?.Message ?? "Discord rate limited.",
                httpStatusCode: statusCode,
                discordErrorCode: error?.Code,
                discordErrorMessage: error?.Message,
                retryAfterSeconds: retryAfter,
                bucket: bucket,
                isGlobal: isGlobal);
        }

        if (statusCode >= 500)
        {
            _logger.LogWarning(
                "Discord {Route} transient failure — status={Status} bucket={Bucket} code={Code}",
                route, statusCode, bucket, error?.Code);

            throw new DiscordTransientException(
                error?.Message ?? $"Discord {route} returned {statusCode}.",
                httpStatusCode: statusCode,
                discordErrorCode: error?.Code,
                discordErrorMessage: error?.Message,
                bucket: bucket);
        }

        if (statusCode == 401)
        {
            _logger.LogError(
                "Discord {Route} unauthorized — bot token may be revoked. code={Code}",
                route, error?.Code);

            throw new DiscordUnauthorizedException(
                error?.Message ?? "Discord rejected bot token.",
                discordErrorCode: error?.Code,
                discordErrorMessage: error?.Message);
        }

        if (statusCode == 403)
        {
            var reason = ClassifyForbidden(forbiddenReasonForRoute, error);

            _logger.LogWarning(
                "Discord {Route} forbidden — reason={Reason} code={Code} message={Message}",
                route, reason, error?.Code, error?.Message);

            throw new DiscordForbiddenException(
                reason,
                error?.Message ?? "Discord forbidden.",
                discordErrorCode: error?.Code,
                discordErrorMessage: error?.Message);
        }

        _logger.LogWarning(
            "Discord {Route} permanent failure — status={Status} code={Code} message={Message}",
            route, statusCode, error?.Code, error?.Message);

        throw new DiscordPermanentException(
            error?.Message ?? $"Discord {route} returned {statusCode}.",
            httpStatusCode: statusCode,
            discordErrorCode: error?.Code,
            discordErrorMessage: error?.Message);
    }

    internal static DiscordForbiddenReason ClassifyForbidden(
        DiscordForbiddenReason routeDefault, DiscordErrorBody? error)
    {
        // 08 §6.4 — sendMessage 403 with code 50007 = "Cannot send
        // messages to this user" (DM closed). Any other 403 against
        // /messages keeps the route default (DmClosed) unless the
        // Discord error code makes it unambiguous; CreateDM 403 always
        // means mutual_guild_required.
        if (routeDefault == DiscordForbiddenReason.DmClosed
            && error?.Code is { } code
            && code != CannotSendMessagesToThisUserCode
            && code != 0)
        {
            // A different Discord error code on /messages means the
            // 403 cause is not the documented "DM closed" path — escalate
            // to admin attention so the cause can be inspected.
            return DiscordForbiddenReason.Unknown;
        }

        return routeDefault;
    }

    private void UpdateRateLimitFromHeaders(HttpResponseMessage response, string bucket)
    {
        // Discord publishes the bucket id on the response so subsequent
        // requests can route through the right semaphore. We pass it
        // straight through to the limiter; the per-bucket gate uses the
        // header value when present and falls back to the route-derived
        // bucket otherwise.
        if (response.Headers.TryGetValues(RateLimitBucketHeader, out var bucketHeaders))
        {
            var bucketHeader = bucketHeaders.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(bucketHeader))
            {
                _rateLimiter.RegisterBucket(bucket, bucketHeader);
            }
        }

        if (response.Headers.TryGetValues(RateLimitResetAfterHeader, out var resetHeaders))
        {
            var raw = resetHeaders.FirstOrDefault();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var resetAfter))
            {
                _rateLimiter.RegisterReset(bucket, resetAfter);
            }
        }
    }

    private static double ResolveRetryAfter(HttpResponseMessage response, DiscordErrorBody? error)
    {
        if (error?.RetryAfter is { } bodyValue && bodyValue > 0)
        {
            return bodyValue;
        }

        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta.TotalSeconds;
        }

        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                return parsed;
            }
        }

        return 1;
    }

    private static async Task<DiscordErrorBody?> TryReadErrorBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<DiscordErrorBody>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private sealed class CreateDmPayload
    {
        [JsonPropertyName("recipient_id")]
        public string RecipientId { get; set; } = string.Empty;
    }

    private sealed class SendMessagePayload
    {
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("allowed_mentions")]
        public AllowedMentionsPayload? AllowedMentions { get; set; }
    }

    private sealed class AllowedMentionsPayload
    {
        public string[] Parse { get; set; } = Array.Empty<string>();
    }

    private sealed class DmChannelResponse
    {
        public string? Id { get; set; }
    }

    private sealed class MessageResponse
    {
        public string? Id { get; set; }
    }
}

/// <summary>
/// Discord error payload — every non-success response carries
/// <c>{ "message": "...", "code": NNNNN, "retry_after": ?, "global": ? }</c>.
/// The fields are nullable because non-rate-limit errors omit
/// <c>retry_after</c> / <c>global</c>.
/// </summary>
public sealed class DiscordErrorBody
{
    public string? Message { get; set; }

    public int? Code { get; set; }

    [JsonPropertyName("retry_after")]
    public double? RetryAfter { get; set; }

    public bool? Global { get; set; }
}

/// <summary>
/// Stable rate-limit bucket identifiers used when Discord hasn't yet
/// published a bucket header for the route. The default partition is
/// <c>createDm</c> + per-channel <c>sendMessage:{channelId}</c>; the
/// real bucket id (when surfaced) is registered onto the same partition
/// via <see cref="IDiscordRateLimiter.RegisterBucket"/>.
/// </summary>
public static class DiscordRateLimitBuckets
{
    public const string CreateDm = "createDm";

    public static string SendMessage(string channelId)
        => string.Concat("sendMessage:", channelId);
}
