using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.API.Controllers;

/// <summary>
/// Transaction lifecycle endpoints — T45 (07 §7.2–§7.4), T46
/// (07 §7.5–§7.6), and T51 cancel (07 §7.7).
/// </summary>
[ApiController]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionEligibilityService _eligibility;
    private readonly ITransactionParamsService _params;
    private readonly ITransactionCreationService _creation;
    private readonly ITransactionDetailService _detail;
    private readonly ITransactionAcceptanceService _acceptance;
    private readonly ITransactionCancellationService _cancellation;

    public TransactionsController(
        ITransactionEligibilityService eligibility,
        ITransactionParamsService @params,
        ITransactionCreationService creation,
        ITransactionDetailService detail,
        ITransactionAcceptanceService acceptance,
        ITransactionCancellationService cancellation)
    {
        _eligibility = eligibility;
        _params = @params;
        _creation = creation;
        _detail = detail;
        _acceptance = acceptance;
        _cancellation = cancellation;
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
                CreateErrorEnvelope(outcome)),

            CreateTransactionStatus.InvalidWallet => BadRequest(
                CreateErrorEnvelope(outcome)),

            CreateTransactionStatus.SanctionsMatch => StatusCode(
                StatusCodes.Status403Forbidden, CreateErrorEnvelope(outcome)),

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
                => UnprocessableEntity(CreateErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>T5 — <c>GET /transactions/:id</c> (07 §7.5).</summary>
    /// <remarks>
    /// Public + authenticated; the service decides which surface to emit
    /// based on the JWT presence. Non-party authenticated callers get
    /// 403 <c>NOT_A_PARTY</c>; unauthenticated callers always get the
    /// trimmed public shape.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [RateLimit("public")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        Guid? callerId = TryGetUserId(out var userId) ? userId : null;
        var callerSteamId = User.FindFirstValue(AuthClaimTypes.SteamId);
        var outcome = await _detail.GetAsync(id, callerId, callerSteamId, cancellationToken);

        return outcome.Status switch
        {
            TransactionDetailStatus.Found => Ok(outcome.Body),

            TransactionDetailStatus.NotFound => NotFound(
                ApiResponse<object>.Fail(
                    outcome.ErrorCode!,
                    outcome.ErrorMessage!,
                    traceId: HttpContext.TraceIdentifier)),

            TransactionDetailStatus.NotAParty => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    outcome.ErrorCode!,
                    outcome.ErrorMessage!,
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>T6 — <c>POST /transactions/:id/accept</c> (07 §7.6).</summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> Accept(
        Guid id,
        [FromBody] AcceptTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                TransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _acceptance.AcceptAsync(userId, id, request, cancellationToken);

        return outcome.Status switch
        {
            AcceptTransactionStatus.Accepted => Ok(outcome.Body),

            AcceptTransactionStatus.NotFound => NotFound(
                AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.NotAParty
                or AcceptTransactionStatus.SteamIdMismatch
                or AcceptTransactionStatus.SanctionsMatch
                or AcceptTransactionStatus.WalletCooldownActive
                => StatusCode(StatusCodes.Status403Forbidden,
                    AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.AlreadyAccepted
                or AcceptTransactionStatus.InvalidStateTransition
                => Conflict(AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.ValidationFailed
                or AcceptTransactionStatus.InvalidWallet
                => BadRequest(AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.BuyerNotFound => Unauthorized(),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>T7 — <c>POST /transactions/:id/cancel</c> (07 §7.7).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                TransactionErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _cancellation.CancelAsync(userId, id, request, cancellationToken);

        return outcome.Status switch
        {
            CancelTransactionStatus.Cancelled => Ok(outcome.Body),

            CancelTransactionStatus.NotFound => NotFound(
                CancelErrorEnvelope(outcome)),

            CancelTransactionStatus.NotAParty => StatusCode(
                StatusCodes.Status403Forbidden,
                CancelErrorEnvelope(outcome)),

            CancelTransactionStatus.PaymentAlreadySent => UnprocessableEntity(
                CancelErrorEnvelope(outcome)),

            CancelTransactionStatus.InvalidStateTransition => Conflict(
                CancelErrorEnvelope(outcome)),

            CancelTransactionStatus.ValidationFailed => BadRequest(
                CancelErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> CreateErrorEnvelope(CreateTransactionOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? TransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Transaction could not be created.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> AcceptErrorEnvelope(AcceptTransactionOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? TransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Transaction could not be accepted.",
            traceId: HttpContext.TraceIdentifier);

    private ApiResponse<object> CancelErrorEnvelope(CancelTransactionOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? TransactionErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Transaction could not be cancelled.",
            traceId: HttpContext.TraceIdentifier);

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }
}
