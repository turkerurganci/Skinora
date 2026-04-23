using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Wallet;

namespace Skinora.API.Controllers;

/// <summary>
/// User profile endpoints — 07 §5.1 (U1), §5.2 (U2), §5.5 (U5),
/// §5.3 (U3), §5.4 (U4).
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    /// <summary>HTTP header name from 07 §4.7 / §5.3 "Ek Auth".</summary>
    private const string ReAuthTokenHeader = "X-ReAuth-Token";

    private readonly IUserProfileService _profileService;
    private readonly IWalletAddressService _walletService;
    private readonly IReAuthTokenValidator _reAuthValidator;

    public UsersController(
        IUserProfileService profileService,
        IWalletAddressService walletService,
        IReAuthTokenValidator reAuthValidator)
    {
        _profileService = profileService;
        _walletService = walletService;
        _reAuthValidator = reAuthValidator;
    }

    /// <summary>U1 — <c>GET /users/me</c>. Own profile for S08 (07 §5.1).</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var dto = await _profileService.GetOwnProfileAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U2 — <c>GET /users/me/stats</c>. Dashboard quick stats (07 §5.2).</summary>
    [HttpGet("me/stats")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UserStatsDto>> GetMyStats(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var dto = await _profileService.GetOwnStatsAsync(userId, cancellationToken);
        if (dto is null) return Unauthorized();

        return Ok(dto);
    }

    /// <summary>U5 — <c>GET /users/{steamId}</c>. Public profile (07 §5.5).</summary>
    [HttpGet("{steamId}")]
    [AllowAnonymous]
    [RateLimit("public")]
    public async Task<ActionResult<PublicUserProfileDto>> GetPublic(
        string steamId, CancellationToken cancellationToken)
    {
        var dto = await _profileService.GetPublicProfileAsync(steamId, cancellationToken);
        if (dto is null)
        {
            var body = ApiResponse<object>.Fail(
                "USER_NOT_FOUND",
                $"User '{steamId}' was not found.",
                traceId: HttpContext.TraceIdentifier);
            return NotFound(body);
        }

        return Ok(dto);
    }

    /// <summary>U3 — <c>PUT /users/me/wallet/seller</c> (07 §5.3).</summary>
    [HttpPut("me/wallet/seller")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public Task<IActionResult> UpdateSellerWallet(
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
        => UpdateWalletAsync(WalletRole.Seller, request, cancellationToken);

    /// <summary>U4 — <c>PUT /users/me/wallet/refund</c> (07 §5.4).</summary>
    [HttpPut("me/wallet/refund")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public Task<IActionResult> UpdateRefundWallet(
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
        => UpdateWalletAsync(WalletRole.Buyer, request, cancellationToken);

    private async Task<IActionResult> UpdateWalletAsync(
        WalletRole role,
        UpdateWalletRequest? request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        if (request is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                WalletErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));
        }

        // 07 §4.7 / §5.3 "Ek Auth": validate re-auth token *before* touching
        // domain state. Token is single-use and bound to a user id — reject
        // with RE_AUTH_TOKEN_INVALID if it fails for any reason (missing
        // header is a different branch handled downstream via RE_AUTH_REQUIRED).
        var reAuthHeader = Request.Headers.TryGetValue(ReAuthTokenHeader, out var headerValues)
            ? headerValues.ToString()
            : null;

        var reAuthValidated = false;
        if (!string.IsNullOrWhiteSpace(reAuthHeader))
        {
            var payload = await _reAuthValidator.ValidateAsync(reAuthHeader, cancellationToken);
            if (payload is null || payload.UserId != userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail(
                    WalletErrorCodes.ReAuthTokenInvalid,
                    "Re-auth token is missing, expired, already consumed, or bound to a different user.",
                    traceId: HttpContext.TraceIdentifier));
            }
            reAuthValidated = true;
        }

        var result = await _walletService.UpdateWalletAsync(
            userId, role, request.WalletAddress, reAuthValidated, cancellationToken);

        return result.Status switch
        {
            WalletUpdateStatus.Success => Ok(new UpdateWalletResponse(
                result.WalletAddress!,
                result.UpdatedAt!.Value,
                result.ActiveTransactionsUsingOldAddress)),

            WalletUpdateStatus.UserNotFound => Unauthorized(),

            WalletUpdateStatus.InvalidAddress => BadRequest(ApiResponse<object>.Fail(
                WalletErrorCodes.InvalidWalletAddress,
                "Wallet address is not a valid Tron (TRC-20) address.",
                traceId: HttpContext.TraceIdentifier)),

            WalletUpdateStatus.SanctionsMatch => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    WalletErrorCodes.SanctionsMatch,
                    "Wallet address matched a sanctions list and was rejected.",
                    traceId: HttpContext.TraceIdentifier)),

            WalletUpdateStatus.ReAuthRequired => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    WalletErrorCodes.ReAuthRequired,
                    "Changing an existing wallet address requires Steam re-verification (X-ReAuth-Token).",
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
