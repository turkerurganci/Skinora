using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Filters;

/// <summary>
/// T166 — opts an endpoint (or a whole controller) out of
/// <see cref="TosAcceptanceGateFilter"/>. Only the session surface
/// (<c>AuthController</c>: <c>/auth/me</c>, <c>/auth/tos/accept</c>,
/// refresh, logout, re-verify, authenticator check) carries it — an account
/// that has not accepted yet must still be able to read its own state, accept,
/// and sign out.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class SkipTosAcceptanceGateAttribute : Attribute;

/// <summary>
/// T166 — server-side enforcement of the Terms of Service + 18+ attestation
/// (02 §21.1, 03 §1 "her ikisi kabul edilmeden devam edemez", 03 §11a.2).
/// </summary>
/// <remarks>
/// <para>
/// Before this filter the rule lived only in the browser: the callback page
/// showed the ToS modal to a <c>new_user</c>, and nothing on the server
/// stopped an account that closed the tab — or called the API directly — from
/// creating and accepting transactions without ever accepting the terms. A
/// returning account that had abandoned the modal was even worse off: the
/// callback reports it as <c>success</c>, so the modal was never shown again.
/// </para>
/// <para>
/// Applies to every MVC endpoint that requires authorization, for every role
/// (admins are Steam accounts too). Skipped for anonymous endpoints, for
/// <see cref="SkipTosAcceptanceGateAttribute"/>, and when the token's
/// <c>sub</c> does not resolve to a user row (the endpoint's own 401/404 path
/// owns that case; the gate is about consent, not existence). The rule itself
/// is <see cref="TosAcceptanceRule"/>, shared with <c>/auth/me</c> so the
/// client shows the modal for exactly the accounts this filter rejects.
/// </para>
/// <para>
/// One primary-key read per authenticated request. Not cached on purpose: at
/// the planned volume the read is negligible, and a cache would be one more
/// thing that could disagree with the database.
/// </para>
/// <para>
/// SignalR hubs (<c>/hubs/*</c>) are outside MVC and therefore outside this
/// filter. They only push events about the caller's own transactions and
/// notifications, which an account cannot have without passing this gate
/// first (known limitation, T166 report).
/// </para>
/// </remarks>
public sealed class TosAcceptanceGateFilter : IAsyncAuthorizationFilter
{
    public const string ErrorCode = "TOS_NOT_ACCEPTED";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any()) return;
        if (!metadata.OfType<IAuthorizeData>().Any()) return;
        if (metadata.OfType<SkipTosAcceptanceGateAttribute>().Any()) return;

        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true) return;
        if (!Guid.TryParse(principal.FindFirstValue(AuthClaimTypes.UserId), out var userId)) return;

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var state = await db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TosAcceptedAt, u.AgeConfirmedAt })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (state is null) return;
        if (TosAcceptanceRule.IsSatisfied(state.TosAcceptedAt, state.AgeConfirmedAt)) return;

        context.Result = new ObjectResult(ApiResponse<object>.Fail(
            ErrorCode,
            "Terms of Service and the 18+ declaration must be accepted before using the platform.",
            traceId: context.HttpContext.TraceIdentifier))
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
