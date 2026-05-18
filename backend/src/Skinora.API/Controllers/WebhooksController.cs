using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Users.Application.Settings;

namespace Skinora.API.Controllers;

/// <summary>
/// External webhook endpoints. Telegram is the only MVP consumer (07 §5.11b);
/// additional providers (SendGrid bounce, Discord gateway) join this
/// controller as they arrive in later phases.
/// </summary>
/// <remarks>
/// Secret-token verification + <c>update_id</c> idempotency live in
/// <see cref="Middleware.TelegramWebhookSignatureMiddleware"/> (T79 —
/// 08 §5.2). By the time this controller runs the request has already
/// passed both gates, so the action only parses + dispatches.
/// </remarks>
[ApiController]
[Route("api/v1/webhooks")]
[RateLimit("auth")]
public sealed partial class WebhooksController : ControllerBase
{
    private readonly ITelegramConnectionService _telegramService;

    public WebhooksController(ITelegramConnectionService telegramService)
    {
        _telegramService = telegramService;
    }

    /// <summary>W1 — <c>POST /webhooks/telegram</c> (07 §5.11b).</summary>
    [HttpPost("telegram")]
    [AllowAnonymous]
    public async Task<IActionResult> Telegram(
        [FromBody] TelegramUpdate? update, CancellationToken cancellationToken)
    {
        if (update?.Message is null)
            return Ok(); // Telegram expects 200 for ignored updates.

        var (code, username, userId) = ExtractPayload(update.Message);
        if (string.IsNullOrWhiteSpace(code))
            return Ok();

        await _telegramService.ProcessWebhookAsync(
            new TelegramWebhookPayload(code, username, userId), cancellationToken);

        // Intentionally always respond 200 — Telegram retries 5xx which would
        // turn a transient parse mismatch into retry storm. The outcome is
        // surfaced to the user via SignalR (05 §7.2 once T62 lands).
        return Ok();
    }

    private static (string? Code, string? Username, long? UserId) ExtractPayload(
        TelegramMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
            return (null, null, null);

        var match = StartCommandRegex().Match(message.Text);
        if (!match.Success)
            return (null, null, null);

        var code = match.Groups[1].Value;
        var username = message.From?.Username;
        var userId = message.From?.Id;
        return (code, username, userId);
    }

    /// <summary>
    /// Matches <c>/start SKN-123456</c> (or any alphanumeric suffix we might
    /// use in the future). The code is case-sensitive to align with the
    /// Redis store keys.
    /// </summary>
    [GeneratedRegex(@"^/start\s+(SKN-[A-Za-z0-9]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StartCommandRegex();
}

public sealed record TelegramUpdate(TelegramMessage? Message);

public sealed record TelegramMessage(string? Text, TelegramFrom? From);

public sealed record TelegramFrom(long Id, string? Username);
