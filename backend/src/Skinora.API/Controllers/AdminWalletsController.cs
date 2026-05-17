using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Wallets;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin wallet-operations endpoint surface (T77 — 05 §3.3). MVP scope: a
/// single endpoint that initiates a manual hot → cold consolidation
/// transfer. Reuses the <c>MANAGE_SETTINGS</c> permission policy because
/// hot wallet limits, addresses and thresholds already live under that
/// scope; a dedicated <c>MANAGE_WALLETS</c> permission is a T-future
/// refinement (would require a contract change to 07 §9.11 and 04 §8.8
/// permission catalog).
/// </summary>
[ApiController]
[Route("api/v1/admin/wallets")]
public sealed class AdminWalletsController : ControllerBase
{
    private const string PolicyManageSettings =
        AuthPolicies.PermissionPrefix + "MANAGE_SETTINGS";

    public const string ErrorInvalidAmount = "INVALID_AMOUNT";
    public const string ErrorInvalidToken = "INVALID_TOKEN";
    public const string ErrorHotWalletNotConfigured = "HOT_WALLET_NOT_CONFIGURED";
    public const string ErrorColdWalletNotConfigured = "COLD_WALLET_NOT_CONFIGURED";
    public const string ErrorSidecarUnavailable = "SIDECAR_UNAVAILABLE";

    private readonly IHotWalletService _hotWallet;

    public AdminWalletsController(IHotWalletService hotWallet)
    {
        _hotWallet = hotWallet;
    }

    /// <summary>
    /// AD20 — <c>POST /admin/wallets/hot-to-cold-transfer</c> (T77, 05 §3.3).
    /// Initiates a TRC-20 transfer from the hot wallet to the
    /// admin-configured cold wallet. On success returns the
    /// <c>ColdWalletTransfer</c> ledger id + tx hash; failures map to
    /// envelope error codes documented above.
    /// </summary>
    [HttpPost("hot-to-cold-transfer")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-write")]
    public async Task<IActionResult> InitiateColdTransfer(
        [FromBody] HotToColdTransferAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(ApiResponse<object>.Fail(
                ErrorInvalidAmount, "Request body is required.",
                traceId: HttpContext.TraceIdentifier));
        }

        if (!Enum.TryParse<StablecoinType>(request.Token, ignoreCase: false, out var token))
        {
            return BadRequest(ApiResponse<object>.Fail(
                ErrorInvalidToken,
                "Token must be one of: USDT, USDC.",
                traceId: HttpContext.TraceIdentifier));
        }

        var adminId = GetCallerUserId();
        if (adminId is null)
        {
            return Unauthorized();
        }

        var outcome = await _hotWallet.InitiateColdTransferAsync(
            request.Amount, token, adminId.Value, cancellationToken);

        return outcome switch
        {
            HotWalletColdTransferOutcome.Success success => Ok(new HotToColdTransferAdminResponse(
                ColdTransferId: success.ColdTransferId,
                TxHash: success.TxHash,
                Amount: success.Amount.ToString("0.######", CultureInfo.InvariantCulture),
                Token: success.Token.ToString(),
                FromAddress: success.FromAddress,
                ToAddress: success.ToAddress)),

            HotWalletColdTransferOutcome.InvalidAmount invalid => BadRequest(
                ApiResponse<object>.Fail(
                    ErrorInvalidAmount,
                    invalid.Reason,
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.HotWalletNotConfigured => UnprocessableEntity(
                ApiResponse<object>.Fail(
                    ErrorHotWalletNotConfigured,
                    "reconciliation.hot_wallet_address SystemSetting is unconfigured or 'NONE'.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.ColdWalletNotConfigured => UnprocessableEntity(
                ApiResponse<object>.Fail(
                    ErrorColdWalletNotConfigured,
                    "reconciliation.cold_wallet_address SystemSetting is unconfigured or 'NONE'.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.SidecarUnavailable unavailable => StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<object>.Fail(
                    ErrorSidecarUnavailable,
                    $"Blockchain sidecar status: {unavailable.Status}. Retry the transfer.",
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private Guid? GetCallerUserId()
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

/// <summary>
/// Request body for <see cref="AdminWalletsController.InitiateColdTransfer"/>.
/// </summary>
public sealed record HotToColdTransferAdminRequest(
    decimal Amount,
    string Token);

/// <summary>
/// 200 response envelope for <see cref="AdminWalletsController.InitiateColdTransfer"/>.
/// </summary>
public sealed record HotToColdTransferAdminResponse(
    long ColdTransferId,
    string TxHash,
    string Amount,
    string Token,
    string FromAddress,
    string ToAddress);
