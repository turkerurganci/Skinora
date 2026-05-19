using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services.AdminSanctions;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin-facing sanctions address list endpoints — T82 (07 §9.23–§9.25
/// AD22/AD23/AD24, 02 §21.1, 03 §11a.3, 06 §3.25). All three actions
/// require <c>MANAGE_SANCTIONS</c> (07 §9.11 — least-privilege ayrı:
/// MANAGE_SETTINGS'ten bağımsız).
/// </summary>
[ApiController]
[Route("api/v1/admin/sanctions/addresses")]
public sealed class AdminSanctionsController : ControllerBase
{
    private const string PolicyManageSanctions =
        AuthPolicies.PermissionPrefix + "MANAGE_SANCTIONS";

    private readonly IAdminSanctionsService _service;

    public AdminSanctionsController(IAdminSanctionsService service)
    {
        _service = service;
    }

    /// <summary>AD22 — <c>GET /admin/sanctions/addresses</c> (07 §9.23).</summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyManageSanctions)]
    [RateLimit("admin-read")]
    public async Task<ActionResult<PagedResult<SanctionedAddressDto>>> List(
        [FromQuery] string? network,
        [FromQuery] string? source,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new AdminSanctionsListQuery(
            Network: network,
            Source: source,
            Search: search,
            IsActive: isActive,
            SortBy: sortBy,
            SortOrder: sortOrder,
            Page: page,
            PageSize: pageSize);

        var result = await _service.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>AD23 — <c>POST /admin/sanctions/addresses</c> (07 §9.24).</summary>
    [HttpPost("")]
    [Authorize(Policy = PolicyManageSanctions)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Add(
        [FromBody] AddSanctionedAddressRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();
        if (request is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.AddAsync(adminId, request, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            AddSanctionedAddressStatus.Added => StatusCode(
                StatusCodes.Status201Created,
                ApiResponse<SanctionedAddressDto>.Ok(outcome.Body!)),

            AddSanctionedAddressStatus.ValidationFailed => BadRequest(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.ValidationError,
                outcome.ErrorMessage ?? "Validation failed.",
                traceId: HttpContext.TraceIdentifier)),

            AddSanctionedAddressStatus.InvalidAddress => BadRequest(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.InvalidWalletAddress,
                outcome.ErrorMessage ?? "Invalid TRC-20 address.",
                traceId: HttpContext.TraceIdentifier)),

            AddSanctionedAddressStatus.AlreadyListed => Conflict(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.AlreadyListed,
                outcome.ErrorMessage ?? "Address is already on the sanctions list.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>AD24 — <c>DELETE /admin/sanctions/addresses/:id</c> (07 §9.25).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyManageSanctions)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetAdminId(out var adminId)) return Unauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _service.DeactivateAsync(adminId, id, ipAddress, cancellationToken);

        return outcome.Status switch
        {
            DeactivateSanctionedAddressStatus.Deactivated => Ok(
                ApiResponse<DeactivateSanctionedAddressResponse>.Ok(outcome.Body!)),

            DeactivateSanctionedAddressStatus.NotFound => NotFound(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.NotFound,
                outcome.ErrorMessage ?? "Sanctioned address not found.",
                traceId: HttpContext.TraceIdentifier)),

            DeactivateSanctionedAddressStatus.AlreadyInactive => Conflict(ApiResponse<object>.Fail(
                AdminSanctionsErrorCodes.AlreadyInactive,
                outcome.ErrorMessage ?? "Sanctioned address is already inactive.",
                traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private bool TryGetAdminId(out Guid adminId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out adminId);
    }
}
