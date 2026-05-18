using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;
using Skinora.Shared.Telegram;

namespace Skinora.API.Middleware;

/// <summary>
/// Secret-token verification + <c>update_id</c> idempotency guard for
/// inbound Telegram webhooks (T79 — 08 §5.2). Sits alongside the Steam
/// + blockchain <see cref="WebhookSignatureMiddleware"/> (HMAC headers)
/// and the Resend <see cref="ResendWebhookSignatureMiddleware"/> (Svix
/// headers); Telegram uses neither so it lives in its own middleware.
/// </summary>
/// <remarks>
/// <para>
/// Behaviour:
/// </para>
/// <list type="number">
///   <item>If <c>Telegram:WebhookSecretToken</c> is unset → 401 (fail closed).</item>
///   <item>If <c>X-Telegram-Bot-Api-Secret-Token</c> is missing or
///         mismatches the configured value (constant-time compare) → 401.</item>
///   <item>Parse the body for <c>update_id</c>. Missing / malformed
///         body → 200 noop (Telegram retries 5xx so we never throw on
///         body parse).</item>
///   <item>INSERT <c>ProcessedNonce(Source="telegram", Nonce=update_id)</c>;
///         duplicate (unique violation) → 200 with idempotent marker.</item>
///   <item>Pass to the next handler with the body stream rewound so the
///         controller's model binder can re-read it.</item>
/// </list>
/// </remarks>
public sealed class TelegramWebhookSignatureMiddleware
{
    public const string TelegramWebhookPathPrefix = "/api/v1/webhooks/telegram";
    public const string TelegramNonceSource = "telegram";
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;

    public TelegramWebhookSignatureMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<TelegramSettings> options,
        AppDbContext db,
        ILogger<TelegramWebhookSignatureMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments(TelegramWebhookPathPrefix))
        {
            await _next(context);
            return;
        }

        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.WebhookSecretToken))
        {
            logger.LogWarning("Telegram webhook rejected — secret_token not configured.");
            await WriteUnauthorizedAsync(
                context,
                "WEBHOOK_UNAUTHORIZED",
                "Telegram webhook secret_token is not configured.");
            return;
        }

        var supplied = context.Request.Headers[SecretHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied) || !ConstantTimeEquals(supplied, settings.WebhookSecretToken))
        {
            logger.LogWarning("Telegram webhook rejected — secret_token mismatch.");
            await WriteUnauthorizedAsync(
                context,
                "WEBHOOK_UNAUTHORIZED",
                "Telegram webhook secret_token mismatch.");
            return;
        }

        context.Request.EnableBuffering();

        string body;
        using (var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }
        context.Request.Body.Position = 0;

        var updateId = TryExtractUpdateId(body);
        if (updateId is null)
        {
            // Body has no update_id (malformed / non-message update we
            // don't care about). Pass through; the controller will
            // respond 200 for any ignored update.
            await _next(context);
            return;
        }

        var nonce = updateId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expiresAt = DateTime.UtcNow.AddHours(Math.Max(settings.IdempotencyTtlHours, 1));

        try
        {
            db.ProcessedNonces.Add(new ProcessedNonce
            {
                Id = Guid.NewGuid(),
                Source = TelegramNonceSource,
                Nonce = nonce,
                ProcessedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogInformation(
                "Telegram webhook duplicate update_id={UpdateId} acknowledged idempotently.",
                nonce);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            var payload = ApiResponse<object>.Ok(new { acknowledged = true, result = "Idempotent" });
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        await _next(context);
    }

    private static long? TryExtractUpdateId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("update_id", out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length
            && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null) return false;
        if (inner is Microsoft.Data.SqlClient.SqlException sql)
        {
            return sql.Number is 2601 or 2627;
        }
        return inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string errorCode, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var payload = ApiResponse<object>.Fail(errorCode, message, traceId: context.TraceIdentifier);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
