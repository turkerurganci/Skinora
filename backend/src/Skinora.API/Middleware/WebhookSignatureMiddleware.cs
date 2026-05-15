using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;

namespace Skinora.API.Middleware;

/// <summary>
/// HMAC-SHA256 signature verification for inbound sidecar webhooks
/// (05 §3.4, 09 §11.3). Applied only to <c>/api/v1/webhooks/steam/*</c>
/// callbacks; the legacy Telegram bot webhook keeps its own header-secret
/// check on <c>/api/v1/webhooks/telegram</c>.
///
/// <para>
/// Steps per 09 §11.3:
/// </para>
/// <list type="number">
///   <item>Headers present: <c>X-Signature</c>, <c>X-Timestamp</c>, <c>X-Nonce</c></item>
///   <item>Timestamp within ±<see cref="WebhookSettings.ReplayWindowSeconds"/> of UTC now</item>
///   <item>Nonce not seen before — atomic INSERT into <c>ProcessedNonces</c>
///         (unique constraint on (Source, Nonce) is the race-safe authority)</item>
///   <item>HMAC-SHA256(timestamp + nonce + body) matches signature
///         (constant-time compare)</item>
/// </list>
/// Any failure responds 401 with an <c>ApiResponse</c>; downstream controller
/// is not reached. On success the request body is rewound so the controller
/// can deserialize it normally.
/// </summary>
public sealed class WebhookSignatureMiddleware
{
    private const string SteamWebhookPathPrefix = "/api/v1/webhooks/steam";
    private const string SignatureHeader = "X-Signature";
    private const string TimestampHeader = "X-Timestamp";
    private const string NonceHeader = "X-Nonce";

    // Source discriminator persisted with each accepted nonce. Distinct value
    // per sidecar prevents nonce collisions if blockchain/notification sidecars
    // share the table (see ProcessedNonce.Source).
    public const string SteamNonceSource = "steam-sidecar";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;

    public WebhookSignatureMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<WebhookSettings> options,
        AppDbContext db,
        ILogger<WebhookSignatureMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments(SteamWebhookPathPrefix))
        {
            await _next(context);
            return;
        }

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SteamSharedSecret))
        {
            logger.LogError("Steam webhook secret is not configured — refusing inbound callback.");
            await WriteUnauthorizedAsync(context, "WEBHOOK_UNAUTHORIZED", "Webhook secret is not configured.");
            return;
        }

        var signature = context.Request.Headers[SignatureHeader].FirstOrDefault();
        var timestamp = context.Request.Headers[TimestampHeader].FirstOrDefault();
        var nonce = context.Request.Headers[NonceHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(nonce))
        {
            logger.LogWarning("Webhook rejected: missing signature/timestamp/nonce headers.");
            await WriteUnauthorizedAsync(context, "WEBHOOK_HEADERS_MISSING", "Required signature headers missing.");
            return;
        }

        if (!DateTime.TryParse(
            timestamp,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var sentAt))
        {
            logger.LogWarning("Webhook rejected: unparsable X-Timestamp value.");
            await WriteUnauthorizedAsync(context, "WEBHOOK_TIMESTAMP_INVALID", "X-Timestamp could not be parsed.");
            return;
        }

        var skewSeconds = Math.Abs((DateTime.UtcNow - sentAt).TotalSeconds);
        if (skewSeconds > settings.ReplayWindowSeconds)
        {
            logger.LogWarning(
                "Webhook rejected: timestamp skew {Skew}s exceeds window {Window}s.",
                skewSeconds, settings.ReplayWindowSeconds);
            await WriteUnauthorizedAsync(context, "WEBHOOK_TIMESTAMP_OUT_OF_WINDOW", "Timestamp outside replay window.");
            return;
        }

        // Enable buffering so signature verification can read the body and the
        // downstream controller can re-read it.
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

        var expected = ComputeSignature(settings.SteamSharedSecret, timestamp, nonce, body);
        if (!FixedTimeEquals(expected, signature))
        {
            logger.LogWarning("Webhook rejected: signature mismatch.");
            await WriteUnauthorizedAsync(context, "WEBHOOK_SIGNATURE_INVALID", "Signature did not match.");
            return;
        }

        // Persist the nonce — unique index on (Source, Nonce) is the authority
        // for replay detection. We *insert first* then catch duplicates: this
        // matches Redis SETNX semantics on SQL Server (no read-then-write race).
        var expiresAt = DateTime.UtcNow.AddSeconds(settings.NonceRetentionSeconds);
        try
        {
            db.ProcessedNonces.Add(new ProcessedNonce
            {
                Id = Guid.NewGuid(),
                Source = SteamNonceSource,
                Nonce = nonce!,
                ProcessedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            logger.LogWarning("Webhook rejected: nonce replay detected ({Nonce}).", nonce);
            await WriteUnauthorizedAsync(context, "WEBHOOK_NONCE_REPLAY", "Nonce already processed.");
            return;
        }

        await _next(context);
    }

    private static string ComputeSignature(string secret, string timestamp, string nonce, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes($"{timestamp}{nonce}{body}");
        var hash = HMACSHA256.HashData(key, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expectedHex, string suppliedHex)
    {
        // Length difference itself is constant-time — comparing CharCount.
        if (expectedHex.Length != suppliedHex.Length)
        {
            return false;
        }
        var expected = Encoding.ASCII.GetBytes(expectedHex);
        var supplied = Encoding.ASCII.GetBytes(suppliedHex.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQL Server: 2601 unique index, 2627 unique constraint.
        // SQLite (test host): "UNIQUE constraint failed" in inner message.
        var inner = ex.InnerException;
        if (inner is null)
        {
            return false;
        }
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
