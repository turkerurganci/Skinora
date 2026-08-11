using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PayoutIssues;

namespace Skinora.API.Controllers;

/// <summary>
/// Transaction lifecycle endpoints — T83a list (07 §7.1), T45
/// (07 §7.2–§7.4), T46 (07 §7.5–§7.6), T51 cancel (07 §7.7), and T60
/// report-payout-issue (07 §7.11).
/// </summary>
[ApiController]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private readonly ITransactionListService _list;
    private readonly ITransactionEligibilityService _eligibility;
    private readonly ITransactionParamsService _params;
    private readonly ITransactionCreationService _creation;
    private readonly ITransactionDetailService _detail;
    private readonly ITransactionAcceptanceService _acceptance;
    private readonly ITransactionCancellationService _cancellation;
    private readonly IPayoutIssueService _payoutIssues;

    public TransactionsController(
        ITransactionListService list,
        ITransactionEligibilityService eligibility,
        ITransactionParamsService @params,
        ITransactionCreationService creation,
        ITransactionDetailService detail,
        ITransactionAcceptanceService acceptance,
        ITransactionCancellationService cancellation,
        IPayoutIssueService payoutIssues)
    {
        _list = list;
        _eligibility = eligibility;
        _params = @params;
        _creation = creation;
        _detail = detail;
        _acceptance = acceptance;
        _cancellation = cancellation;
        _payoutIssues = payoutIssues;
    }

    /// <summary>T1 — <c>GET /transactions</c> (07 §7.1, T83a).</summary>
    /// <remarks>
    /// <para>
    /// Returns the caller's own transactions (seller or buyer) filtered by
    /// <c>tab</c>: <c>active</c>, <c>completed</c>, <c>cancelled</c>. The
    /// query parameter is optional; an unset or unrecognised value defaults
    /// to <c>active</c> per the 11 §T83a kabul kriteri.
    /// </para>
    /// <para>
    /// Pagination defaults: <c>page=1</c>, <c>pageSize=20</c> (clamped 1–100
    /// at the service). Order is <c>CreatedAt DESC</c>.
    /// </para>
    /// </remarks>
    [HttpGet("")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<IActionResult> List(
        [FromQuery] string? tab,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resolvedTab = ParseTab(tab);
        var query = new TransactionListQuery(resolvedTab, page, pageSize);
        var result = await _list.ListAsync(userId, query, cancellationToken);
        return Ok(result);
    }

    private static TransactionListTab ParseTab(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TransactionListTab.Active;
        return raw.Trim().ToLowerInvariant() switch
        {
            "active" => TransactionListTab.Active,
            "completed" => TransactionListTab.Completed,
            "cancelled" => TransactionListTab.Cancelled,
            _ => TransactionListTab.Active,
        };
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
                // T121 — 422 INVENTORY_PRIVATE: same status and code the
                // inventory listing endpoint uses for the same condition
                // (07 §6.1), so the seller sees one vocabulary across the
                // create flow.
                or CreateTransactionStatus.InventoryPrivate
                => UnprocessableEntity(CreateErrorEnvelope(outcome)),

            // T121 — 503 STEAM_UNAVAILABLE: the inventory check is undecided,
            // not negative. Retryable, mirroring the accept endpoint's
            // fail-closed 503 (07 §7.6).
            CreateTransactionStatus.SteamUnavailable => StatusCode(
                StatusCodes.Status503ServiceUnavailable, CreateErrorEnvelope(outcome)),

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

    /// <summary>T46 — <c>GET /transactions/by-invite/:token</c> (07 §7.5a).</summary>
    /// <remarks>
    /// Resolves the OPEN_LINK opaque invite token to the public-invite consume
    /// surface (04 §7.3 public variant). Public + authenticated; an
    /// authenticated token holder who is not yet a party is treated as a
    /// prospective buyer (<c>canAccept=true</c>). Accept stays id-based via
    /// <c>POST /transactions/:id/accept</c>. The literal <c>by-invite</c>
    /// segment never collides with the <c>{id:guid}</c> detail route.
    /// </remarks>
    [HttpGet("by-invite/{token}")]
    [AllowAnonymous]
    [RateLimit("public")]
    public async Task<IActionResult> GetByInvite(string token, CancellationToken cancellationToken)
    {
        Guid? callerId = TryGetUserId(out var userId) ? userId : null;
        var callerSteamId = User.FindFirstValue(AuthClaimTypes.SteamId);
        var outcome = await _detail.GetByInviteTokenAsync(token, callerId, callerSteamId, cancellationToken);

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
                or AcceptTransactionStatus.AccountFlagged
                // T119a — 403 MOBILE_AUTHENTICATOR_REQUIRED (07 §7.6).
                or AcceptTransactionStatus.MobileAuthenticatorRequired
                => StatusCode(StatusCodes.Status403Forbidden,
                    AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.AlreadyAccepted
                or AcceptTransactionStatus.InvalidStateTransition
                => Conflict(AcceptErrorEnvelope(outcome)),

            AcceptTransactionStatus.ValidationFailed
                or AcceptTransactionStatus.InvalidWallet
                // T119a — 400 INVALID_TRADE_URL (07 §7.6). 400 (not the 422 the
                // U17 profile-save path returns) because 07 §7.6 pins it there.
                or AcceptTransactionStatus.InvalidTradeUrl
                => BadRequest(AcceptErrorEnvelope(outcome)),

            // T119a — 503 STEAM_UNAVAILABLE: Steam could not confirm the buyer's
            // Mobile Authenticator, so acceptance fails closed (08 §2.2) and the
            // caller may retry. Same code/status as 07 §7.6a confirm-ready.
            AcceptTransactionStatus.SteamUnavailable
                => StatusCode(StatusCodes.Status503ServiceUnavailable,
                    AcceptErrorEnvelope(outcome)),

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

            CancelTransactionStatus.AccountSuspended => StatusCode(
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

    /// <summary>T11 — <c>POST /transactions/:id/report-payout-issue</c> (07 §7.11).</summary>
    [HttpPost("{id:guid}/report-payout-issue")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> ReportPayoutIssue(
        Guid id,
        [FromBody] ReportPayoutIssueRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (request is null)
            return BadRequest(ApiResponse<object>.Fail(
                PayoutIssueErrorCodes.ValidationError,
                "Request body is required.",
                traceId: HttpContext.TraceIdentifier));

        var outcome = await _payoutIssues.ReportAsync(userId, id, request, cancellationToken);

        return outcome.Status switch
        {
            ReportPayoutIssueStatus.Reported => Created(
                $"/api/v1/transactions/{id:D}/report-payout-issue/{outcome.Body!.IssueId:D}",
                outcome.Body),

            ReportPayoutIssueStatus.NotFound => NotFound(
                ReportPayoutIssueErrorEnvelope(outcome)),

            ReportPayoutIssueStatus.NotSeller => StatusCode(
                StatusCodes.Status403Forbidden,
                ReportPayoutIssueErrorEnvelope(outcome)),

            ReportPayoutIssueStatus.TransactionNotCompleted
                or ReportPayoutIssueStatus.IssueAlreadyReported
                => Conflict(ReportPayoutIssueErrorEnvelope(outcome)),

            ReportPayoutIssueStatus.ValidationFailed => BadRequest(
                ReportPayoutIssueErrorEnvelope(outcome)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private ApiResponse<object> ReportPayoutIssueErrorEnvelope(ReportPayoutIssueOutcome outcome) =>
        ApiResponse<object>.Fail(
            outcome.ErrorCode ?? PayoutIssueErrorCodes.ValidationError,
            outcome.ErrorMessage ?? "Payout issue could not be reported.",
            traceId: HttpContext.TraceIdentifier);

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }
}
