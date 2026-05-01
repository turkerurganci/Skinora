using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;
using Skinora.Notifications.Application.Inbox;
using Skinora.Shared.Models;

namespace Skinora.API.Controllers;

/// <summary>
/// Platform-in-app notification inbox endpoints (07 §8.1–§8.4 / 05 §7.2).
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationInboxService _inbox;

    public NotificationsController(INotificationInboxService inbox)
    {
        _inbox = inbox;
    }

    /// <summary>N1 — <c>GET /notifications</c>. Paginated bildirim list (07 §8.1).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<PagedResult<NotificationListItemDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var result = await _inbox.ListAsync(userId, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>N2 — <c>GET /notifications/unread-count</c> (07 §8.2).</summary>
    [HttpGet("unread-count")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-read")]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var count = await _inbox.GetUnreadCountAsync(userId, cancellationToken);
        return Ok(new UnreadCountResponse(count));
    }

    /// <summary>N3 — <c>POST /notifications/mark-all-read</c> (07 §8.3).</summary>
    [HttpPost("mark-all-read")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<ActionResult<MarkAllReadResponse>> MarkAllRead(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var marked = await _inbox.MarkAllReadAsync(userId, cancellationToken);
        return Ok(new MarkAllReadResponse(marked));
    }

    /// <summary>N4 — <c>PUT /notifications/:id/read</c> (07 §8.4).</summary>
    [HttpPut("{id:guid}/read")]
    [Authorize(Policy = AuthPolicies.Authenticated)]
    [RateLimit("user-write")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var outcome = await _inbox.MarkReadAsync(userId, id, cancellationToken);
        return outcome switch
        {
            MarkReadOutcome.Success => Ok((object?)null),

            MarkReadOutcome.NotFound => NotFound(ApiResponse<object>.Fail(
                NotificationInboxErrorCodes.NotificationNotFound,
                $"Notification '{id}' was not found.",
                traceId: HttpContext.TraceIdentifier)),

            MarkReadOutcome.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(
                    NotificationInboxErrorCodes.Forbidden,
                    "You do not have access to this notification.",
                    traceId: HttpContext.TraceIdentifier)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(AuthClaimTypes.UserId);
        return Guid.TryParse(claim, out userId);
    }
}
