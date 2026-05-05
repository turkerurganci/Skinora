using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Disputes.Application.Disputes;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Buyer-facing dispute endpoints — T58 (07 §7.8–§7.10, 02 §10, 03 §6).
/// All three actions are buyer-only, atomic, and rate-limited via the
/// <c>user-write</c> bucket (per-user). The duplicate-type rule is enforced
/// at the DB layer via the unfiltered <c>UQ_Disputes_TransactionId_Type</c>
/// index.
/// </summary>
[ApiController]
[Route("api/v1/transactions/{id:guid}/disputes")]
public sealed class DisputesController : ControllerBase
{
    private readonly IDisputeService _service;

    public DisputesController(IDisputeService service)
    {
        _service = service;
    }

    /// <summary>T8 — <c>POST /transactions/:id/disputes</c> (07 §7.8).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> Open(
        Guid id,
        [FromBody] OpenDisputeRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                DisputeErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _service.OpenAsync(userId, id, request, cancellationToken);

        return outcome.Status switch
        {
            OpenDisputeStatus.Opened => Ok(outcome.Body),

            OpenDisputeStatus.NotFound => NotFound(OpenErrorEnvelope(outcome)),

            OpenDisputeStatus.NotBuyer => StatusCode(
                StatusCodes.Status403Forbidden, OpenErrorEnvelope(outcome)),

            OpenDisputeStatus.InvalidStateTransition
                or OpenDisputeStatus.DuplicateDispute
                => Conflict(OpenErrorEnvelope(outcome)),

            OpenDisputeStatus.ValidationFailed => BadRequest(OpenErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>T9 — <c>POST /transactions/:id/disputes/:disputeId/submit-txhash</c> (07 §7.9).</summary>
    [HttpPost("{disputeId:guid}/submit-txhash")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> SubmitTxHash(
        Guid id,
        Guid disputeId,
        [FromBody] SubmitTxHashRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                DisputeErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _service.SubmitTxHashAsync(
            userId, id, disputeId, request, cancellationToken);

        return outcome.Status switch
        {
            SubmitTxHashStatus.Processed => Ok(outcome.Body),

            SubmitTxHashStatus.NotFound => NotFound(SubmitErrorEnvelope(outcome)),

            SubmitTxHashStatus.NotBuyer => StatusCode(
                StatusCodes.Status403Forbidden, SubmitErrorEnvelope(outcome)),

            SubmitTxHashStatus.NotPaymentDispute => UnprocessableEntity(
                SubmitErrorEnvelope(outcome)),

            SubmitTxHashStatus.DisputeClosed => Conflict(SubmitErrorEnvelope(outcome)),

            SubmitTxHashStatus.ValidationFailed => BadRequest(
                SubmitErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>T10 — <c>POST /transactions/:id/disputes/:disputeId/escalate</c> (07 §7.10).</summary>
    [HttpPost("{disputeId:guid}/escalate")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> Escalate(
        Guid id,
        Guid disputeId,
        [FromBody] EscalateDisputeRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                DisputeErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _service.EscalateAsync(
            userId, id, disputeId, request, cancellationToken);

        return outcome.Status switch
        {
            EscalateDisputeStatus.Escalated => Ok(outcome.Body),

            EscalateDisputeStatus.NotFound => NotFound(EscalateErrorEnvelope(outcome)),

            EscalateDisputeStatus.NotBuyer => StatusCode(
                StatusCodes.Status403Forbidden, EscalateErrorEnvelope(outcome)),

            EscalateDisputeStatus.AlreadyEscalated
                or EscalateDisputeStatus.DisputeClosed
                => Conflict(EscalateErrorEnvelope(outcome)),

            EscalateDisputeStatus.ValidationFailed => BadRequest(
                EscalateErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> OpenErrorEnvelope(OpenDisputeOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? DisputeErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Dispute could not be opened.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> SubmitErrorEnvelope(SubmitTxHashOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? DisputeErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Tx hash submission failed.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> EscalateErrorEnvelope(EscalateDisputeOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? DisputeErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Dispute could not be escalated.",
            traceId: HttpContext.TraceIdentifier);

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }
}
