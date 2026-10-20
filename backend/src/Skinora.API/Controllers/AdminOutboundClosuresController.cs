using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services.WalletClosures;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// AD33–AD37 — the two-person tool that closes an Ethereum outbound row with
/// the hash of a transaction already on chain (T157, 07 §9.32; DEPLOY_RUNBOOK
/// §D adım 7.4). ⚠ PARA. Every action needs <c>MANAGE_SETTINGS</c> — the
/// permission the other wallet operations (AD39) already use; a dedicated
/// wallet permission would be a catalog change (07 §9.11, 04 §8.8) and is left
/// as a refinement. The proposer and the approver are told apart by the
/// authenticated admin id, never by anything in the request body.
/// </summary>
[ApiController]
[Route("api/v1/admin/wallets/outbound-closures")]
public sealed class AdminOutboundClosuresController : ControllerBase
{
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";

    private readonly IOutboundRowClosureService _service;

    public AdminOutboundClosuresController(IOutboundRowClosureService service)
    {
        _service = service;
    }

    /// <summary>AD33 — <c>GET /admin/wallets/outbound-closures/candidates</c>.</summary>
    [HttpGet("candidates")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> Candidates(CancellationToken cancellationToken) =>
        Ok(await _service.ListCandidatesAsync(cancellationToken));

    /// <summary>AD34 — <c>GET /admin/wallets/outbound-closures</c>.</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> List(
        [FromQuery] OutboundRowClosureStatus? status, CancellationToken cancellationToken) =>
        Ok(await _service.ListAsync(status, cancellationToken));

    /// <summary>AD35 — <c>POST /admin/wallets/outbound-closures</c> (propose).</summary>
    [HttpPost("")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Propose(
        [FromBody] ProposeOutboundClosureRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null) return Map(ClosureResult.Fail(ClosureError.Validation, "Request body is required."));

        return Map(await _service.ProposeAsync(
            adminId, request.BlockchainTransactionId, request.TxHash, request.Reason,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }

    /// <summary>AD36 — <c>POST /admin/wallets/outbound-closures/:id/approve</c> (a different admin).</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody] ApproveOutboundClosureRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();

        return Map(await _service.ApproveAsync(
            adminId, id, request?.TxHash,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }

    /// <summary>AD37 — <c>POST /admin/wallets/outbound-closures/:id/reject</c> (also withdraws one's own proposal).</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] RejectOutboundClosureRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();

        return Map(await _service.RejectAsync(
            adminId, id, request?.Note,
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken));
    }

    private IActionResult Map(ClosureResult result)
    {
        if (result.Error is null) return Ok(result.Value);

        var (status, code) = result.Error.Value switch
        {
            ClosureError.Validation => (StatusCodes.Status400BadRequest, "VALIDATION_ERROR"),
            ClosureError.InvalidHash => (StatusCodes.Status400BadRequest, "INVALID_TX_HASH"),
            ClosureError.HashMismatch => (StatusCodes.Status400BadRequest, "HASH_CONFIRMATION_MISMATCH"),
            ClosureError.SelfApproval => (StatusCodes.Status403Forbidden, "SELF_APPROVAL_FORBIDDEN"),
            ClosureError.RowNotFound => (StatusCodes.Status404NotFound, "ROW_NOT_FOUND"),
            ClosureError.ClosureNotFound => (StatusCodes.Status404NotFound, "CLOSURE_NOT_FOUND"),
            ClosureError.RowNotEthereum => (StatusCodes.Status422UnprocessableEntity, "ROW_NOT_ETHEREUM"),
            ClosureError.RowNotOutbound => (StatusCodes.Status422UnprocessableEntity, "ROW_NOT_OUTBOUND"),
            ClosureError.HashNotSuccessful => (StatusCodes.Status422UnprocessableEntity, "HASH_NOT_SUCCESSFUL"),
            ClosureError.HashNotFound => (StatusCodes.Status422UnprocessableEntity, "HASH_NOT_FOUND"),
            ClosureError.HashDoesNotMatchRow => (StatusCodes.Status422UnprocessableEntity, "HASH_DOES_NOT_MATCH_ROW"),
            ClosureError.RowTransferUnresolved => (StatusCodes.Status422UnprocessableEntity, "ROW_TRANSFER_UNRESOLVED"),
            ClosureError.AmbiguousRowMatch => (StatusCodes.Status409Conflict, "AMBIGUOUS_ROW_MATCH"),
            ClosureError.RowNotOpen => (StatusCodes.Status409Conflict, "ROW_NOT_OPEN"),
            ClosureError.ClosureNotOpen => (StatusCodes.Status409Conflict, "CLOSURE_NOT_OPEN"),
            ClosureError.HashAlreadyBound => (StatusCodes.Status409Conflict, "HASH_ALREADY_BOUND"),
            ClosureError.OpenClosureExists => (StatusCodes.Status409Conflict, "CLOSURE_ALREADY_PROPOSED"),
            ClosureError.HashNotFinal => (StatusCodes.Status409Conflict, "HASH_NOT_FINAL"),
            ClosureError.Conflict => (StatusCodes.Status409Conflict, "CLOSURE_CONFLICT"),
            ClosureError.ChainUnavailable => (StatusCodes.Status502BadGateway, "CHAIN_SERVICE_UNAVAILABLE"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR"),
        };

        return StatusCode(status, ApiResponse<object>.Fail(
            code, result.Message ?? code, traceId: HttpContext.TraceIdentifier));
    }

    private bool TryGetAdminId(out Guid adminId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out adminId);
    }
}

public sealed record ProposeOutboundClosureRequest(
    Guid BlockchainTransactionId,
    string? TxHash,
    string? Reason);

public sealed record ApproveOutboundClosureRequest(string? TxHash);

public sealed record RejectOutboundClosureRequest(string? Note);
