using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.Middleware;
using Skinora.Shared.Enums;
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
/// <remarks>
/// <para>
/// <b>Network (T148 — 05 §3.3a "Webhook yükü").</b> Every body names its
/// network. The controller rejects — before the handler runs — a body whose
/// <c>network</c> is missing or unknown (400 <c>WEBHOOK_NETWORK_INVALID</c>;
/// one exception: a body with no <c>network</c> field signed with TRON's
/// secret is taken as TRON, the pre-T148 sidecar's shape) or
/// differs from the network whose secret signed the request (409
/// <c>WEBHOOK_NETWORK_MISMATCH</c>); the handler then rejects one that differs
/// from the referenced deposit's <c>PaymentAddress.Network</c> (409, same
/// code). Nothing is written in any of the three cases and all are logged; the
/// sidecar treats a 4xx as terminal, which is right — re-sending cannot make
/// the two sides agree.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/webhooks/blockchain")]
[AllowAnonymous]
public sealed class BlockchainWebhooksController : ControllerBase
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>400 — the body names no known network.</summary>
    public const string NetworkInvalidCode = "WEBHOOK_NETWORK_INVALID";

    /// <summary>409 — the body's network differs from the signer's or the deposit's.</summary>
    public const string NetworkMismatchCode = "WEBHOOK_NETWORK_MISMATCH";

    private readonly IBlockchainWebhookHandler _handler;
    private readonly ILogger<BlockchainWebhooksController> _logger;

    public BlockchainWebhooksController(
        IBlockchainWebhookHandler handler,
        ILogger<BlockchainWebhooksController> logger)
    {
        _handler = handler;
        _logger = logger;
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
    /// Finality reached by the network's rule (TRON 20 blocks, Ethereum
    /// `finalized` — BlockchainNetworkPolicy, 08 §3.4 / 05 §3.3). Backend
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

    /// <summary>
    /// Late buyer transfer detected at a cancelled transaction's deposit
    /// address (T75 — 02 §4.4 gecikmeli ödeme). Backend persists the
    /// incoming row + queues a <c>LATE_PAYMENT_REFUND</c> via the T73
    /// refund pipeline.
    /// </summary>
    [HttpPost("late-payment-detected")]
    public Task<IActionResult> LatePaymentDetected(
        [FromBody] BlockchainWebhookEnvelope<LatePaymentDetectedData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandleLatePaymentDetectedAsync, cancellationToken);

    /// <summary>
    /// Sidecar post-cancel monitor advanced to the next state — backend
    /// mirrors <c>PaymentAddress.MonitoringStatus</c> (T75 — 06 §2.16).
    /// </summary>
    [HttpPost("post-cancel-monitor-state-changed")]
    public Task<IActionResult> PostCancelMonitorStateChanged(
        [FromBody] BlockchainWebhookEnvelope<PostCancelMonitorStateChangedData> envelope,
        CancellationToken cancellationToken)
        => DispatchAsync(envelope, _handler.HandlePostCancelMonitorStateChangedAsync, cancellationToken);

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

        var correlationId = ResolveCorrelationId();

        var authenticated =
            HttpContext.Items.TryGetValue(WebhookSignatureMiddleware.AuthenticatedNetworkItemKey, out var raw)
            && raw is BlockchainNetwork signer
                ? signer
                : (BlockchainNetwork?)null;

        // T148 düzeltme turu 1 (F2) — a body WITHOUT a network field that was
        // signed with TRON's secret is TRON's: before T148 the TRON service
        // sent no network, and the sidecar's request-body rule is the same
        // (field absent → accepted). Without this, a backend deployed ahead of
        // its sidecar would 400 every TRON callback, and the sidecar treats a
        // 4xx as terminal — a real payment event would be lost. Only absence
        // is defaulted, only for the TRON signer: an explicit wrong or unknown
        // value is still 400/409, and the Ethereum service must always name
        // its network.
        if (envelope.Network is null && authenticated == BlockchainNetwork.TRON)
        {
            _logger.LogWarning(
                "Blockchain webhook {Event} has no network field — signed with TRON's secret, taken as TRON (pre-T148 sidecar). correlationId={CorrelationId}",
                envelope.Event, correlationId);
            envelope.Network = nameof(BlockchainNetwork.TRON);
        }

        if (!BlockchainWebhookHandler.TryParseNetwork(envelope.Network, out var claimed))
        {
            _logger.LogWarning(
                "Blockchain webhook {Event} rejected — network '{Network}' is missing or unknown. correlationId={CorrelationId}",
                envelope.Event, envelope.Network, correlationId);
            return BadRequest(ApiResponse<object>.Fail(
                NetworkInvalidCode,
                "Webhook body must name its network (TRON or ETHEREUM).",
                traceId: HttpContext.TraceIdentifier));
        }

        // The secret that signed the request decides who the caller is
        // (WebhookSignatureMiddleware). A body claiming another network is a
        // misrouted or forged callback: one service may only speak for itself.
        if (authenticated is not null && authenticated != claimed)
        {
            _logger.LogError(
                "Blockchain webhook {Event} REJECTED — body claims {Claimed} but was signed with the {Authenticated} service's secret. correlationId={CorrelationId}",
                envelope.Event, claimed, authenticated, correlationId);
            return NetworkMismatch();
        }

        var result = await handler(envelope, correlationId, cancellationToken);
        if (result == BlockchainWebhookResult.NetworkMismatch)
        {
            return NetworkMismatch();
        }

        return Ok(ApiResponse<object>.Ok(new { acknowledged = true, result = result.ToString() }));
    }

    private ObjectResult NetworkMismatch() =>
        Conflict(ApiResponse<object>.Fail(
            NetworkMismatchCode,
            "Webhook network does not match the referenced record.",
            traceId: HttpContext.TraceIdentifier));

    private string ResolveCorrelationId()
    {
        var fromHeader = Request.Headers[CorrelationIdHeader].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(fromHeader) ? fromHeader : HttpContext.TraceIdentifier;
    }
}
