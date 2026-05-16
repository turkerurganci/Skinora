using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.Shared.Models;
using Skinora.Steam.Application.Webhooks;

namespace Skinora.API.Controllers;

/// <summary>
/// T68 — Steam sidecar → backend webhook callbacks. Signature/timestamp/nonce
/// validation lives in <c>WebhookSignatureMiddleware</c>; this controller only
/// runs after a request has been authenticated, so the actions assume a
/// trusted body.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/steam")]
[AllowAnonymous]
public sealed class SteamWebhooksController : ControllerBase
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly ISteamWebhookHandler _handler;

    public SteamWebhooksController(ISteamWebhookHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Bot lifecycle envelope from the sidecar's BotManager publisher (T64).
    /// </summary>
    [HttpPost("bot-events")]
    public async Task<IActionResult> BotEvents(
        [FromBody] SteamWebhookEnvelope<BotEventData> envelope,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "WEBHOOK_PAYLOAD_MISSING",
                "Request body could not be deserialized.",
                traceId: HttpContext.TraceIdentifier));
        }

        await _handler.HandleBotEventAsync(envelope, ResolveCorrelationId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { acknowledged = true }));
    }

    /// <summary>
    /// Trade offer envelope from the sidecar (T65 send, T66 status change).
    /// </summary>
    [HttpPost("trade-events")]
    public async Task<IActionResult> TradeEvents(
        [FromBody] SteamWebhookEnvelope<TradeOfferEventData> envelope,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "WEBHOOK_PAYLOAD_MISSING",
                "Request body could not be deserialized.",
                traceId: HttpContext.TraceIdentifier));
        }

        var result = await _handler.HandleTradeEventAsync(envelope, ResolveCorrelationId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { acknowledged = true, result = result.ToString() }));
    }

    private string ResolveCorrelationId()
    {
        var fromHeader = Request.Headers[CorrelationIdHeader].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(fromHeader) ? fromHeader : HttpContext.TraceIdentifier;
    }
}
