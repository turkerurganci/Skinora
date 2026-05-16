using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.API.Controllers;

/// <summary>
/// T71 — blockchain sidecar → backend webhook callbacks. Signature /
/// timestamp / nonce validation lives in <c>WebhookSignatureMiddleware</c>
/// (extended to cover <c>/api/v1/webhooks/blockchain</c> in T71; the
/// Steam scope from T68 remains). This controller only runs after a
/// request has been authenticated, so the actions assume a trusted body.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/blockchain")]
[AllowAnonymous]
public sealed class BlockchainWebhooksController : ControllerBase
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IBlockchainWebhookHandler _handler;

    public BlockchainWebhooksController(IBlockchainWebhookHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// First sighting of an incoming payment (08 §3.4 phase 1). Backend
    /// persists a <c>BlockchainTransaction</c> row at <c>Status=DETECTED</c>.
    /// </summary>
    [HttpPost("payment-detected")]
    public Task<IActionResult> PaymentDetected(
        [FromBody] BlockchainWebhookEnvelope<PaymentDetectedData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandlePaymentDetectedAsync, cancellationToken);

    /// <summary>
    /// Finality reached (20-block confirmation, 08 §3.4 / 05 §3.3). Backend
    /// flips the existing row to <c>Status=CONFIRMED</c>.
    /// </summary>
    [HttpPost("payment-confirmed")]
    public Task<IActionResult> PaymentConfirmed(
        [FromBody] BlockchainWebhookEnvelope<PaymentConfirmedData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandlePaymentConfirmedAsync, cancellationToken);

    /// <summary>
    /// Supported stablecoin received that differs from the expected token
    /// (08 §3.4 wrong-token table). Backend records the row and surfaces
    /// it for the refund pipeline (T72/T73).
    /// </summary>
    [HttpPost("wrong-token")]
    public Task<IActionResult> WrongToken(
        [FromBody] BlockchainWebhookEnvelope<WrongTokenIncomingData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandleWrongTokenIncomingAsync, cancellationToken);

    /// <summary>
    /// Unsupported token received (08 §3.4 spam policy). Backend records
    /// the row at terminal <c>CONFIRMED</c> for audit; no refund attempted.
    /// </summary>
    [HttpPost("spam-token")]
    public Task<IActionResult> SpamToken(
        [FromBody] BlockchainWebhookEnvelope<SpamTokenIncomingData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandleSpamTokenIncomingAsync, cancellationToken);

    private async Task<IActionResult> DispatchAsync<TData>(
        BlockchainWebhookEnvelope<TData>? envelope,
        Func<BlockchainWebhookEnvelope<TData>, string, CancellationToken, Task<BlockchainWebhookResult>> handler,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                "WEBHOOK_PAYLOAD_MISSING",
                "Request body could not be deserialized.",
                traceId: HttpContext.TraceIdentifier));
        }

        var result = await handler(envelope, ResolveCorrelationId(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { acknowledged = true, result = result.ToString() }));
    }

    private string ResolveCorrelationId()
    {
        var fromHeader = Request.Headers[CorrelationIdHeader].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(fromHeader) ? fromHeader : HttpContext.TraceIdentifier;
    }
}
