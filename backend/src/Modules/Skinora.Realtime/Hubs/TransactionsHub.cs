using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Auth.Configuration;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Realtime.Hubs;

/// <summary>
/// SignalR hub serving the transaction-detail real-time channel
/// (T61 — 07 §11.1 RT1, mounted at <c>/hubs/transactions</c>).
/// </summary>
/// <remarks>
/// <para>
/// Authentication is enforced by the framework <see cref="AuthorizeAttribute"/>:
/// the JWT bearer pipeline is configured (T61 — <c>RealtimeModule</c>) to
/// accept the token from a <c>?access_token=</c> query parameter on hub
/// connection requests so the SignalR JS client can connect through WebSockets
/// without custom headers (07 §11.1 "Auth: JWT query param").
/// </para>
/// <para>
/// Group naming uses <see cref="GroupName"/>. Server-to-client broadcasts
/// (<c>TransactionStatusChanged</c>, <c>CountdownSync</c>, <c>PaymentDetected</c>,
/// <c>PaymentConfirmed</c>, <c>DisputeUpdate</c>, <c>FlagResolved</c>,
/// <c>EmergencyHoldApplied</c>, <c>EmergencyHoldReleased</c>) target the
/// per-transaction group rather than a per-user one because both buyer and
/// seller subscribe to the same room while the detail page is open.
/// </para>
/// <para>
/// Membership is enforced on <see cref="JoinTransaction(Guid)"/> by checking
/// that the caller is the buyer or the seller of the transaction. Admins
/// (WP9 — T61 K3) are permitted via the JWT role claim
/// (<see cref="HubClaims.IsAdmin"/>) so the admin transaction-detail surface
/// (S16) can subscribe to live updates for any transaction.
/// </para>
/// </remarks>
[Authorize]
public class TransactionsHub : Hub
{
    public const string GroupPrefix = "tx:";

    private readonly AppDbContext _db;
    private readonly ILogger<TransactionsHub> _logger;

    public TransactionsHub(AppDbContext db, ILogger<TransactionsHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Stable group name for a given transaction id.</summary>
    public static string GroupName(Guid transactionId) => $"{GroupPrefix}{transactionId:N}";

    /// <summary>
    /// Join the per-transaction broadcast group. Throws <see cref="HubException"/>
    /// when the caller is not a participant of the transaction. The detail page
    /// (S07) calls this on mount.
    /// </summary>
    public async Task JoinTransaction(Guid transactionId)
    {
        if (transactionId == Guid.Empty)
        {
            throw new HubException("transactionId is required.");
        }

        var userId = ResolveUserId();

        var participation = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Id == transactionId)
            .Select(t => new { t.SellerId, t.BuyerId })
            .SingleOrDefaultAsync(Context.ConnectionAborted);

        if (participation is null)
        {
            throw new HubException("TRANSACTION_NOT_FOUND");
        }

        // WP9 (T61 K3) — admins join any transaction room (the admin
        // transaction-detail surface, S16, subscribes to the same hub). Role is
        // read from the JWT claim, matching the permission gate the admin REST
        // endpoints already enforce.
        if (participation.SellerId != userId
            && participation.BuyerId != userId
            && !HubClaims.IsAdmin(Context.User))
        {
            _logger.LogWarning(
                "TransactionsHub join refused: user {UserId} is not a participant of transaction {TransactionId}.",
                userId, transactionId);
            throw new HubException("TRANSACTION_FORBIDDEN");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(transactionId), Context.ConnectionAborted);
    }

    /// <summary>
    /// Leave the per-transaction broadcast group. Idempotent; the detail page
    /// calls this on unmount.
    /// </summary>
    public Task LeaveTransaction(Guid transactionId)
    {
        if (transactionId == Guid.Empty)
        {
            return Task.CompletedTask;
        }
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId, GroupName(transactionId), Context.ConnectionAborted);
    }

    private Guid ResolveUserId()
    {
        var raw = Context.User?.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(raw, out var userId))
        {
            throw new HubException("AUTH_INVALID");
        }
        return userId;
    }
}
