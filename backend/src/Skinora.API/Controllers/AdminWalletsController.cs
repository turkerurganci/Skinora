using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.API.Services.AdminWallets;
using Skinora.Auth.Configuration;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Wallets;

namespace Skinora.API.Controllers;

/// <summary>
/// Admin wallet-operations endpoint surface (T77 — 05 §3.3). One endpoint that
/// initiates a manual hot → cold consolidation transfer on a network (T157:
/// the body names it; Ethereum moves USDT only). Reuses the <c>MANAGE_SETTINGS</c> permission policy because
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
    public const string ErrorInvalidNetwork = "INVALID_NETWORK";
    public const string ErrorNetworkUnavailable = "NETWORK_UNAVAILABLE";
    public const string ErrorTransferDeferred = "TRANSFER_DEFERRED";

    private readonly IHotWalletService _hotWallet;
    private readonly IAdminWalletOverviewService _overview;

    public AdminWalletsController(IHotWalletService hotWallet, IAdminWalletOverviewService overview)
    {
        _hotWallet = hotWallet;
        _overview = overview;
    }

    /// <summary>
    /// AD38 — <c>GET /admin/wallets</c> (T157, 07 §9.33): the platform's wallets
    /// (hot, cold, and Ethereum's fuel wallet), their balances and the outbound
    /// and cold-transfer activity, one block per network. Balances come from
    /// each network's own service; a service that does not answer shows as
    /// <c>UNAVAILABLE</c> for that network only. Same permission as the
    /// settings screen that shows the wallet addresses read-only (AD8).
    /// </summary>
    [HttpGet("")]
    [Authorize(Policy = PolicyManageSettings)]
    [RateLimit("admin-read")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken) =>
        Ok(await _overview.GetAsync(cancellationToken));

    /// <summary>
    /// AD39 — <c>POST /admin/wallets/hot-to-cold-transfer</c> (T77 comments call it "AD20"; 07 §9.34, 05 §3.3).
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

        // 07 §1.3 — an omitted network is TRON (the pre-F8 body); a value that
        // is not a known network is a 400, never a guess.
        var network = BlockchainNetwork.TRON;
        if (request.Network is not null
            && !Enum.TryParse(request.Network, ignoreCase: false, out network))
        {
            return BadRequest(ApiResponse<object>.Fail(
                ErrorInvalidNetwork,
                "Network must be one of: TRON, ETHEREUM.",
                traceId: HttpContext.TraceIdentifier));
        }

        var adminId = GetCallerUserId();
        if (adminId is null)
        {
            return Unauthorized();
        }

        var outcome = await _hotWallet.InitiateColdTransferAsync(
            network, request.Amount, token, adminId.Value, cancellationToken);

        return outcome switch
        {
            HotWalletColdTransferOutcome.Success success => Ok(new HotToColdTransferAdminResponse(
                ColdTransferId: success.ColdTransferId,
                TxHash: success.TxHash,
                Amount: success.Amount.ToString("0.######", CultureInfo.InvariantCulture),
                Token: success.Token.ToString(),
                FromAddress: success.FromAddress,
                ToAddress: success.ToAddress,
                Network: success.Network.ToString())),

            HotWalletColdTransferOutcome.InvalidAmount invalid => BadRequest(
                ApiResponse<object>.Fail(
                    ErrorInvalidAmount,
                    invalid.Reason,
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.HotWalletNotConfigured => UnprocessableEntity(
                ApiResponse<object>.Fail(
                    ErrorHotWalletNotConfigured,
                    "HOT_WALLET_ADDRESS is not configured on the backend. It is deployment "
                    + "configuration shared with the blockchain sidecar, not a panel setting "
                    + "(05 §3.3) — set it in .env and restart both services.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.ColdWalletNotConfigured => UnprocessableEntity(
                ApiResponse<object>.Fail(
                    ErrorColdWalletNotConfigured,
                    "COLD_WALLET_ADDRESS is not configured, so consolidation is disabled. It is "
                    + "deployment configuration shared with the blockchain sidecar, not a panel "
                    + "setting (05 §3.3) — set it in .env and restart both services.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.SidecarUnavailable unavailable => StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<object>.Fail(
                    ErrorSidecarUnavailable,
                    $"Blockchain sidecar status: {unavailable.Status}. Retry the transfer.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.NetworkNotConfigured notConfigured => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail(
                    ErrorNetworkUnavailable,
                    $"The {notConfigured.Network} blockchain service is not configured; nothing was sent.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.TokenNotSupported unsupported => BadRequest(
                ApiResponse<object>.Fail(
                    ErrorInvalidToken,
                    $"{unsupported.Network} moves USDT only.",
                    traceId: HttpContext.TraceIdentifier)),

            HotWalletColdTransferOutcome.Deferred deferred => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail(
                    ErrorTransferDeferred,
                    $"The service did not sign the transfer for now ({deferred.Reason}); nothing was sent. Try again later.",
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
    string Token,
    string? Network = null);

/// <summary>
/// 200 response envelope for <see cref="AdminWalletsController.InitiateColdTransfer"/>.
/// </summary>
public sealed record HotToColdTransferAdminResponse(
    long ColdTransferId,
    string TxHash,
    string Amount,
    string Token,
    string FromAddress,
    string ToAddress,
    string Network);
