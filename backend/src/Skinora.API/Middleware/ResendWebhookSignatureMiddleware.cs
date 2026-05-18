using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Shared.Email;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;

namespace Skinora.API.Middleware;

/// <summary>
/// Svix-style signature verification + replay / idempotency check for
/// inbound Resend webhooks (T78 — 08 §4.3). Parallel to
/// <see cref="WebhookSignatureMiddleware"/> (which guards the
/// HMAC-headers used by the Steam + blockchain sidecars); Resend's
/// Svix headers (<c>svix-id</c>, <c>svix-timestamp</c>,
/// <c>svix-signature</c>) carry a different signing format so they get
/// their own middleware to keep concerns separated.
/// </summary>
public sealed class ResendWebhookSignatureMiddleware
{
    public const string ResendWebhookPathPrefix = "/api/v1/webhooks/resend";
    public const string ResendNonceSource = "resend";

    private const string SvixIdHeader = "svix-id";
    private const string SvixTimestampHeader = "svix-timestamp";
    private const string SvixSignatureHeader = "svix-signature";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;
    private readonly SvixSignatureVerifier _verifier;

    public ResendWebhookSignatureMiddleware(RequestDelegate next, SvixSignatureVerifier verifier)
    {
        _next = next;
        _verifier = verifier;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<ResendSettings> options,
        AppDbContext db,
        ILogger<ResendWebhookSignatureMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments(ResendWebhookPathPrefix))
        {
            await _next(context);
            return;
        }

        var settings = options.Value;
        var svixId = context.Request.Headers[SvixIdHeader].FirstOrDefault();
        var svixTimestamp = context.Request.Headers[SvixTimestampHeader].FirstOrDefault();
        var svixSignature = context.Request.Headers[SvixSignatureHeader].FirstOrDefault();

        // Enable buffering so signature verification + the downstream
        // controller can both read the body.
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

        var verifyResult = _verifier.Verify(
            webhookSigningSecret: settings.WebhookSigningSecret,
            svixId: svixId,
            svixTimestamp: svixTimestamp,
            svixSignature: svixSignature,
            rawBody: body,
            replayWindowSeconds: settings.WebhookReplayWindowSeconds,
            utcNow: DateTime.UtcNow);

        if (verifyResult != SvixSignatureVerifier.VerifyResult.Valid)
        {
            logger.LogWarning(
                "Resend webhook rejected — reason={Reason} svixId={SvixId}",
                verifyResult,
                svixId);
            await WriteUnauthorizedAsync(context, MapErrorCode(verifyResult), MapErrorMessage(verifyResult));
            return;
        }

        // svixId is non-null when verify returns Valid (checked inside the
        // verifier), but the compiler can't see that — re-assign locally.
        var nonce = svixId!;

        // Idempotency / replay — reuse the ProcessedNonces table the
        // sidecar webhooks already lean on. INSERT first; the unique
        // index on (Source, Nonce) is the authority.
        var expiresAt = DateTime.UtcNow.AddSeconds(
            Math.Max(settings.WebhookReplayWindowSeconds * 12, 3600));

        try
        {
            db.ProcessedNonces.Add(new ProcessedNonce
            {
                Id = Guid.NewGuid(),
                Source = ResendNonceSource,
                Nonce = nonce,
                ProcessedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Svix retransmitted an event we've already processed —
            // return 200 with an idempotent marker so Resend stops
            // resending.
            logger.LogInformation(
                "Resend webhook duplicate svix-id={SvixId} acknowledged idempotently.",
                nonce);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            var payload = ApiResponse<object>.Ok(new { acknowledged = true, result = "Idempotent" });
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        await _next(context);
    }

    private static string MapErrorCode(SvixSignatureVerifier.VerifyResult result) => result switch
    {
        SvixSignatureVerifier.VerifyResult.InvalidSecretConfiguration => "WEBHOOK_UNAUTHORIZED",
        SvixSignatureVerifier.VerifyResult.MissingHeaders => "WEBHOOK_HEADERS_MISSING",
        SvixSignatureVerifier.VerifyResult.TimestampUnparseable => "WEBHOOK_TIMESTAMP_INVALID",
        SvixSignatureVerifier.VerifyResult.TimestampOutOfWindow => "WEBHOOK_TIMESTAMP_OUT_OF_WINDOW",
        SvixSignatureVerifier.VerifyResult.SignatureMismatch => "WEBHOOK_SIGNATURE_INVALID",
        _ => "WEBHOOK_UNAUTHORIZED",
    };

    private static string MapErrorMessage(SvixSignatureVerifier.VerifyResult result) => result switch
    {
        SvixSignatureVerifier.VerifyResult.InvalidSecretConfiguration => "Resend webhook signing secret is not configured.",
        SvixSignatureVerifier.VerifyResult.MissingHeaders => "Required Svix headers missing.",
        SvixSignatureVerifier.VerifyResult.TimestampUnparseable => "svix-timestamp could not be parsed.",
        SvixSignatureVerifier.VerifyResult.TimestampOutOfWindow => "Timestamp outside replay window.",
        SvixSignatureVerifier.VerifyResult.SignatureMismatch => "Signature did not match.",
        _ => "Webhook rejected.",
    };

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
