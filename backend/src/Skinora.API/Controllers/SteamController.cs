using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Steam.Application.Inventory;

namespace Skinora.API.Controllers;

/// <summary>
/// Steam-side endpoints exposed to the SPA (07 §6 — S1).
/// </summary>
/// <remarks>
/// The current authenticated user's <c>steam_id</c> claim is the source of
/// truth for the lookup (no path parameter); the sidecar handles the actual
/// Steam Community fetch, pagination and 120-second Redis cache.
/// </remarks>
[ApiController]
[Route("api/v1/steam")]
public sealed class SteamController : ControllerBase
{
    /// <summary>Error code published when the sidecar reports a private profile (07 §6.1).</summary>
    public const string InventoryPrivateErrorCode = "INVENTORY_PRIVATE";

    /// <summary>Error code surfaced on sidecar / upstream Steam failures (07 §6.1).</summary>
    public const string SteamUnavailableErrorCode = "STEAM_UNAVAILABLE";

    private readonly ISteamInventoryQueryService _inventoryService;

    public SteamController(ISteamInventoryQueryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>S1 — <c>GET /steam/inventory</c>. Caller's own CS2 inventory (07 §6.1).</summary>
    [HttpGet("inventory")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("steam-inventory")]
    public async Task<ActionResult<SteamInventoryDto>> GetInventory(
        CancellationToken cancellationToken)
    {
        var steamId = User.FindFirstValue(AuthClaimTypes.SteamId);
        if (string.IsNullOrWhiteSpace(steamId))
            return Unauthorized();

        var result = await _inventoryService.GetForSteamIdAsync(steamId, cancellationToken);
        return result.Status switch
        {
            GetInventoryStatus.Success when result.Inventory is { } inv => Ok(inv),

            GetInventoryStatus.InventoryPrivate => UnprocessableEntity(ApiResponse<object>.Fail(
                InventoryPrivateErrorCode,
                "Steam inventory is private. Profile must be public to read items.",
                traceId: HttpContext.TraceIdentifier)),

            GetInventoryStatus.SteamUnavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail(
                    SteamUnavailableErrorCode,
                    "Steam inventory service is temporarily unavailable. Please retry shortly.",
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
