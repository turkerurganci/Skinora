using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.Notifications.Application.Webhooks;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// T78 — inbound Resend webhook callbacks for bounce / complaint /
/// delivery-delayed / suppressed / failed events (08 §4.3). Svix
/// signature, replay-window, idempotency (svix-id) checks all live in
/// <see cref="Middleware.ResendWebhookSignatureMiddleware"/>; by the
/// time control reaches this controller the request body is trusted
/// and unique.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/resend")]
[AllowAnonymous]
public sealed class ResendWebhooksController : ControllerBase
{
    private const string SvixIdHeader = "svix-id";

    private readonly IResendWebhookHandler _handler;

    public ResendWebhooksController(IResendWebhookHandler handler)
    {
        _handler = handler;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(
        [FromBody] ResendWebhookEnvelope? envelope,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "WEBHOOK_PAYLOAD_MISSING",
                "Request body could not be deserialized.",
                traceId: HttpContext.TraceIdentifier));
        }

        var svixId = Request.Headers[SvixIdHeader].FirstOrDefault() ?? string.Empty;

        var result = await _handler.HandleAsync(envelope, svixId, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { acknowledged = true, result = result.ToString() }));
    }
}
