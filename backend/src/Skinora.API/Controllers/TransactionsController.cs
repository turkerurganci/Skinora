using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.API.Controllers;

/// <summary>
/// Transaction lifecycle endpoints — T45 (07 §7.2–§7.4). Detail
/// (<c>GET /transactions/:id</c>), accept (<c>POST /transactions/:id/accept</c>)
/// and cancel (<c>POST /transactions/:id/cancel</c>) endpoints arrive in T46
/// and T51.
/// </summary>
[ApiController]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionEligibilityService _eligibility;
    private readonly ITransactionParamsService _params;
    private readonly ITransactionCreationService _creation;

    public TransactionsController(
        ITransactionEligibilityService eligibility,
        ITransactionParamsService @params,
        ITransactionCreationService creation)
    {
        _eligibility = eligibility;
        _params = @params;
        _creation = creation;
    }

    /// <summary>T3 — <c>GET /transactions/eligibility</c> (07 §7.3).</summary>
    [HttpGet("eligibility")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<EligibilityDto>> GetEligibility(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var dto = await _eligibility.GetAsync(userId, cancellationToken);
        return Ok(dto);
    }

    /// <summary>T4 — <c>GET /transactions/params</c> (07 §7.4).</summary>
    [HttpGet("params")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<TransactionParamsDto>> GetParams(CancellationToken cancellationToken)
    {
        var dto = await _params.GetAsync(cancellationToken);
        return Ok(dto);
    }

    /// <summary>T2 — <c>POST /transactions</c> (07 §7.2).</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> Create(
        [FromBody] CreateTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                TransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _creation.CreateAsync(userId, request, cancellationToken);

        return outcome.Status switch
        {
            CreateTransactionStatus.Created => Created(
                $"/api/v1/transactions/{outcome.Body!.Id:D}",
                outcome.Body),

            CreateTransactionStatus.ValidationFailed => BadRequest(
                ErrorEnvelope(outcome)),

            CreateTransactionStatus.InvalidWallet => BadRequest(
                ErrorEnvelope(outcome)),

            CreateTransactionStatus.SanctionsMatch => StatusCode(
                StatusCodes.Status403Forbidden, ErrorEnvelope(outcome)),

            CreateTransactionStatus.SellerNotFound => Unauthorized(),

            CreateTransactionStatus.EligibilityFailed
                or CreateTransactionStatus.OpenLinkDisabled
                or CreateTransactionStatus.ItemNotInInventory
                or CreateTransactionStatus.ItemNotTradeable
                or CreateTransactionStatus.PriceOutOfRange
                or CreateTransactionStatus.TimeoutOutOfRange
                or CreateTransactionStatus.BuyerSteamIdNotFound
                or CreateTransactionStatus.PayoutAddressCooldownActive
                or CreateTransactionStatus.SellerWalletAddressMissing
                => UnprocessableEntity(ErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> ErrorEnvelope(CreateTransactionOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? TransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Transaction could not be created.",
            traceId: HttpContext.TraceIdentifier);

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }
}
