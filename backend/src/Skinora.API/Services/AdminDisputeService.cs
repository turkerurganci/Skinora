using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Disputes.Application.Admin;
using Skinora.Disputes.Application.Disputes;
using Skinora.Disputes.Domain.Entities;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services;

/// <summary>
/// WP5 / T58 — production <see cref="IAdminDisputeService"/>. Closes the
/// ESCALATED dead-end (02 §10.4, 03 §6.4): the admin queue (AD27), detail
/// (AD28) and resolve command (AD29). Lives at the API composition root because
/// resolution orchestrates <c>Skinora.Disputes</c> (Dispute row),
/// <c>Skinora.Transactions</c> (state machine + refund/return events) and
/// <c>Skinora.Platform</c> (audit) — modules that cannot reference each other
/// without a cycle. All resolution side effects land inside a single
/// <see cref="DbContext.SaveChangesAsync"/> so the action is atomic (09 §13.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Seller-favor</b> upholds the transaction: the dispute closes to
/// RESOLVED_FOR_SELLER and <see cref="Transaction.HasActiveDispute"/> clears
/// (when no other OPEN/ESCALATED dispute remains), unblocking the WP1
/// <c>SellerPayoutQueueJob</c> at ITEM_DELIVERED. No state-machine transition.
/// </para>
/// <para>
/// <b>Buyer-favor</b> unwinds the escrow: the dispute closes to
/// RESOLVED_FOR_BUYER and the transaction fires <c>AdminResolveRefund</c> →
/// REFUNDED (terminal, so the payout job can never pick it up). When the buyer
/// had paid, a <see cref="PaymentRefundToBuyerRequestedEvent"/> queues the
/// WP2 refund; when the item was still on the platform, an
/// <see cref="ItemRefundToSellerRequestedEvent"/> returns it. At ITEM_DELIVERED
/// the item is already with the buyer — physical claw-back is a separate manual
/// / WP6 process (07 §9.x exceptional resolution).
/// </para>
/// <para>
/// A transaction under emergency hold must have the hold released first (AD19c)
/// — mirrors the AD19 guard; dispute and hold are independent admin axes.
/// </para>
/// </remarks>
public sealed class AdminDisputeService : IAdminDisputeService
{
    /// <summary>AdminNote bounds — column width is 2000 (06 §3.11).</summary>
    public const int MinNoteLength = 1;
    public const int MaxNoteLength = 2000;

    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _clock;

    public AdminDisputeService(
        AppDbContext db,
        IOutboxService outbox,
        IAuditLogger audit,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _audit = audit;
        _clock = clock;
    }

    // ---------- AD27 — GET /admin/disputes ----------

    public async Task<PagedResult<AdminDisputeListItemDto>> ListAsync(
        AdminDisputeListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Default to the ESCALATED queue — the disputes waiting on an admin.
        var status = query.Status ?? DisputeStatus.ESCALATED;
        var (safePage, safePageSize) = ClampPaging(query.Page, query.PageSize);

        var baseQuery = _db.Set<Dispute>()
            .AsNoTracking()
            .Where(d => d.Status == status);
        if (query.Type is { } type)
            baseQuery = baseQuery.Where(d => d.Type == type);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var rows = await baseQuery
            .OrderByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(d => new DisputeListProjection
            {
                Id = d.Id,
                TransactionId = d.TransactionId,
                Type = d.Type,
                Status = d.Status,
                OpenedByUserId = d.OpenedByUserId,
                CreatedAt = d.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Transaction item/status (include soft-deleted so the dispute still
        // surfaces) + opener party — resolved via dictionary joins.
        var txIds = rows.Select(r => r.TransactionId).Distinct().ToList();
        var txById = await _db.Set<Transaction>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => txIds.Contains(t.Id))
            .Select(t => new { t.Id, t.ItemName, t.Status })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var partyById = await LoadPartiesAsync(
            rows.Select(r => r.OpenedByUserId).Distinct().ToList(), cancellationToken);

        var items = rows.Select(r =>
        {
            txById.TryGetValue(r.TransactionId, out var tx);
            return new AdminDisputeListItemDto(
                Id: r.Id,
                TransactionId: r.TransactionId,
                Type: r.Type,
                Status: r.Status,
                ItemName: tx?.ItemName ?? string.Empty,
                TransactionStatus: tx?.Status ?? default,
                OpenedBy: partyById.GetValueOrDefault(r.OpenedByUserId)
                          ?? UnknownParty(r.OpenedByUserId),
                CreatedAt: r.CreatedAt);
        }).ToList();

        return new PagedResult<AdminDisputeListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    // ---------- AD28 — GET /admin/disputes/:id ----------

    public async Task<AdminDisputeDetailDto?> GetAsync(
        Guid disputeId, CancellationToken cancellationToken)
    {
        var dispute = await _db.Set<Dispute>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        if (dispute is null) return null;

        var tx = await _db.Set<Transaction>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == dispute.TransactionId, cancellationToken);
        if (tx is null) return null;

        var seller = await LoadPartyAsync(tx.SellerId, cancellationToken)
                     ?? UnknownParty(tx.SellerId);
        var buyer = tx.BuyerId.HasValue
            ? await LoadPartyAsync(tx.BuyerId.Value, cancellationToken)
              ?? UnknownParty(tx.BuyerId.Value)
            : null;

        return new AdminDisputeDetailDto(
            Id: dispute.Id,
            Type: dispute.Type,
            Status: dispute.Status,
            SystemCheckResult: dispute.SystemCheckResult,
            UserDescription: dispute.UserDescription,
            AdminId: dispute.AdminId,
            AdminNote: dispute.AdminNote,
            ResolvedAt: dispute.ResolvedAt,
            CreatedAt: dispute.CreatedAt,
            UpdatedAt: dispute.UpdatedAt,
            Transaction: new AdminDisputeTransactionDto(
                Id: tx.Id,
                Status: tx.Status,
                ItemName: tx.ItemName,
                Price: tx.Price,
                Stablecoin: tx.StablecoinType,
                IsOnHold: tx.IsOnHold,
                HasActiveDispute: tx.HasActiveDispute,
                Seller: seller,
                Buyer: buyer));
    }

    // ---------- AD29 — POST /admin/disputes/:id/resolve ----------

    public async Task<AdminResolveDisputeOutcome> ResolveAsync(
        Guid adminUserId,
        Guid disputeId,
        AdminResolveDisputeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: validation ----------
        var note = (request.AdminNote ?? string.Empty).Trim();
        if (note.Length < MinNoteLength)
            return Fail(AdminResolveDisputeStatus.ValidationFailed,
                DisputeErrorCodes.ValidationError, "adminNote is required (07 §9.x).");
        if (note.Length > MaxNoteLength)
            return Fail(AdminResolveDisputeStatus.ValidationFailed,
                DisputeErrorCodes.ValidationError,
                $"adminNote must be at most {MaxNoteLength} characters (07 §9.x).");

        // Defensive enum range guard — JsonStringEnumConverter permits integer
        // values, so an out-of-range outcome could otherwise bind silently
        // (T103b-2 F1 lesson).
        if (!Enum.IsDefined(request.Outcome))
            return Fail(AdminResolveDisputeStatus.ValidationFailed,
                DisputeErrorCodes.ValidationError, "Unknown resolution outcome.");

        // ---------- Stage 2: load dispute (tracked) ----------
        var dispute = await _db.Set<Dispute>()
            .FirstOrDefaultAsync(d => d.Id == disputeId, cancellationToken);
        if (dispute is null)
            return Fail(AdminResolveDisputeStatus.NotFound,
                DisputeErrorCodes.DisputeNotFound, "Dispute not found.");

        // ---------- Stage 3: ESCALATED guard ----------
        if (dispute.Status != DisputeStatus.ESCALATED)
            return Fail(AdminResolveDisputeStatus.NotEscalated,
                DisputeErrorCodes.NotEscalated,
                "Only escalated disputes can be resolved (07 §9.x).");

        // ---------- Stage 4: load transaction (tracked) ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == dispute.TransactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Fail(AdminResolveDisputeStatus.NotFound,
                DisputeErrorCodes.TransactionNotFound, "Transaction not found.");

        // ---------- Stage 5: emergency-hold guard (release first via AD19c) ----------
        if (transaction.IsOnHold)
            return Fail(AdminResolveDisputeStatus.TransactionOnHold,
                DisputeErrorCodes.TransactionOnHold,
                "Transaction is under emergency hold; release the hold first (AD19c).");

        var now = _clock.GetUtcNow().UtcDateTime;
        var previousTxStatus = transaction.Status;
        var buyerRefunded = false;

        // ---------- Stage 6: outcome-specific side effects ----------
        if (request.Outcome == DisputeResolutionOutcome.BUYER_FAVOR)
        {
            var paymentReceived = transaction.PaymentReceivedAt is not null;

            var cancelReason = $"Dispute çözümü (alıcı lehine): {note}";
            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            try
            {
                machine.Fire(TransactionTrigger.AdminResolveRefund, new CancellationContext(cancelReason));
            }
            catch (DomainException ex)
            {
                return Fail(AdminResolveDisputeStatus.InvalidStateTransition, ex.ErrorCode, ex.Message);
            }

            // WP15 — audit-trail row (06 §3.6) for the buyer-favor REFUNDED
            // resolution. Admin actor; REFUNDED is excluded from reputation
            // (not in the 06 §3.1 responsibility map) — history only.
            TransactionHistoryRecorder.Record(
                _db, transaction, previousTxStatus, TransactionTrigger.AdminResolveRefund,
                ActorType.ADMIN, adminUserId, now);

            // v3.0 — no item return branch exists. The platform never holds the
            // item, so a buyer-favour ruling can only move money. The
            // consequence is deliberate and stated in 02 §10: once delivery is
            // proven, ruling for the buyer shifts the loss onto the seller with
            // no way to recover the item, which is why the documented default
            // at that point is a seller-favour ruling.

            // Payment refund to the buyer when they had paid (gates on
            // PaymentReceivedAt — true at ITEM_DELIVERED, unlike the status-based
            // AD19 helper which excludes ITEM_DELIVERED).
            if (paymentReceived && transaction.BuyerId is { } buyerId
                && !string.IsNullOrEmpty(transaction.BuyerRefundAddress))
            {
                await _outbox.PublishAsync(
                    new PaymentRefundToBuyerRequestedEvent(
                        EventId: Guid.NewGuid(),
                        TransactionId: transaction.Id,
                        BuyerId: buyerId,
                        BuyerRefundAddress: transaction.BuyerRefundAddress,
                        OccurredAt: now),
                    cancellationToken);
                buyerRefunded = true;
            }

            dispute.Status = DisputeStatus.RESOLVED_FOR_BUYER;
        }
        else
        {
            // Seller-favor — uphold the transaction; no state transition. Clearing
            // HasActiveDispute (Stage 7) lets the WP1 payout proceed.
            dispute.Status = DisputeStatus.RESOLVED_FOR_SELLER;
        }

        // ---------- Stage 7: dispute resolution fields + active-flag ----------
        dispute.AdminId = adminUserId;
        dispute.AdminNote = note;
        dispute.ResolvedAt = now;
        dispute.UpdatedAt = now;

        await UpdateActiveDisputeFlagAsync(transaction, dispute.Id, cancellationToken);

        // ---------- Stage 8: notification + audit ----------
        await _outbox.PublishAsync(
            new DisputeResolvedEvent(
                EventId: Guid.NewGuid(),
                DisputeId: dispute.Id,
                TransactionId: transaction.Id,
                Type: dispute.Type,
                SellerId: transaction.SellerId,
                BuyerId: dispute.OpenedByUserId,
                Outcome: request.Outcome,
                BuyerRefunded: buyerRefunded,
                OccurredAt: now),
            cancellationToken);

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.DISPUTE_RESOLVED,
                EntityType: nameof(Dispute),
                EntityId: dispute.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new
                {
                    Status = DisputeStatus.ESCALATED.ToString(),
                    TransactionStatus = previousTxStatus.ToString(),
                }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    Status = dispute.Status.ToString(),
                    Outcome = request.Outcome.ToString(),
                    TransactionStatus = transaction.Status.ToString(),
                    BuyerRefunded = buyerRefunded,
                    Note = note,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        // ---------- Stage 9: atomic commit ----------
        await _db.SaveChangesAsync(cancellationToken);

        return new AdminResolveDisputeOutcome(
            Status: AdminResolveDisputeStatus.Resolved,
            Body: new AdminResolveDisputeResponse(
                Id: dispute.Id,
                Status: dispute.Status,
                TransactionStatus: transaction.Status,
                ResolvedAt: now,
                BuyerRefunded: buyerRefunded),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- helpers ----------

    private async Task UpdateActiveDisputeFlagAsync(
        Transaction transaction, Guid currentDisputeId, CancellationToken cancellationToken)
    {
        // Active = OPEN or ESCALATED only. The current dispute is being mutated
        // to a resolved terminal in-flight; exclude it so the probe reflects the
        // post-commit state. 03 §6 allows concurrent different-type disputes, so
        // resolving one must not clear the flag while a sibling stays active.
        var otherActiveExist = await _db.Set<Dispute>()
            .AnyAsync(
                d => d.TransactionId == transaction.Id
                     && d.Id != currentDisputeId
                     && (d.Status == DisputeStatus.OPEN || d.Status == DisputeStatus.ESCALATED),
                cancellationToken);

        transaction.HasActiveDispute = otherActiveExist;
    }

    private async Task<Dictionary<Guid, AdminDisputePartyDto>> LoadPartiesAsync(
        IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, AdminDisputePartyDto>();

        var users = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.SteamId, u.SteamDisplayName })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => new AdminDisputePartyDto(u.Id, u.SteamId, u.SteamDisplayName));
    }

    private async Task<AdminDisputePartyDto?> LoadPartyAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AdminDisputePartyDto(u.Id, u.SteamId, u.SteamDisplayName))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Fallback for a deleted / anonymized opener (02 §19).</summary>
    private static AdminDisputePartyDto UnknownParty(Guid userId) =>
        new(userId, SteamId: null, DisplayName: "Deleted User");


    private static (int Page, int PageSize) ClampPaging(int page, int pageSize)
    {
        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize
            : pageSize;
        return (safePage, safePageSize);
    }

    private static AdminResolveDisputeOutcome Fail(
        AdminResolveDisputeStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private sealed class DisputeListProjection
    {
        public Guid Id { get; init; }
        public Guid TransactionId { get; init; }
        public DisputeType Type { get; init; }
        public DisputeStatus Status { get; init; }
        public Guid OpenedByUserId { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
