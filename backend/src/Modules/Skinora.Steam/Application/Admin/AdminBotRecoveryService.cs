using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Steam.Application.Admin;

/// <inheritdoc cref="IAdminBotRecoveryService"/>
public sealed class AdminBotRecoveryService : IAdminBotRecoveryService
{
    /// <summary>Matches the <c>BotRecoveryItem.AdminNote</c> column length.</summary>
    public const int MaxNoteLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _clock;

    public AdminBotRecoveryService(AppDbContext db, IAuditLogger audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<BotRecoveryQueueResponse?> GetQueueAsync(
        Guid botId, CancellationToken cancellationToken)
    {
        var bot = await _db.Set<PlatformSteamBot>()
            .AsNoTracking()
            .Where(b => b.Id == botId && !b.IsDeleted)
            .Select(b => new { b.Id, b.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return null;
        }

        var items = await ProjectItems(
                _db.Set<BotRecoveryItem>().AsNoTracking().Where(r => r.PlatformSteamBotId == botId))
            .ToListAsync(cancellationToken);

        // Oldest first — the longest-stuck items sit at the top of the queue.
        var ordered = items.OrderBy(i => i.CreatedAt).ToList();

        return new BotRecoveryQueueResponse(bot.Id, bot.Status, ordered);
    }

    public async Task<UpdateRecoveryItemOutcome> UpdateAsync(
        Guid adminUserId,
        Guid recoveryItemId,
        UpdateRecoveryItemRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = await _db.Set<BotRecoveryItem>()
            .FirstOrDefaultAsync(r => r.Id == recoveryItemId, cancellationToken);
        if (item is null)
        {
            return Failure(UpdateRecoveryItemStatus.NotFound,
                BotRecoveryErrorCodes.RecoveryItemNotFound, "Recovery item not found.");
        }

        // RESOLVED is terminal — keep the forensic record immutable.
        if (item.RecoveryStatus == BotRecoveryStatus.RESOLVED)
        {
            return Failure(UpdateRecoveryItemStatus.AlreadyResolved,
                BotRecoveryErrorCodes.AlreadyResolved,
                "Recovery item is already resolved and cannot be modified.");
        }

        var changesRequested =
            request.RecoveryStatus.HasValue
            || request.ResponsibleAdminId.HasValue
            || request.AdminNote is not null;
        if (!changesRequested)
        {
            return Failure(UpdateRecoveryItemStatus.ValidationFailed,
                BotRecoveryErrorCodes.NoChange,
                "At least one of recoveryStatus, responsibleAdminId or adminNote is required.");
        }

        // Reject out-of-range enum values before any mutation. JsonStringEnumConverter
        // binds with allowIntegerValues:true by default, so {"recoveryStatus":99}
        // deserialises to an undefined enum that would persist a garbage status and
        // pollute the live RecoveryTransactionCount / FailoverStatus metrics (a 99 is
        // counted as "open" because it is not RESOLVED). 07 §9.29 mandates
        // VALIDATION_ERROR here; mirrors the TransactionCreationService stage-1 guard.
        if (request.RecoveryStatus is { } requestedStatus && !Enum.IsDefined(requestedStatus))
        {
            return Failure(UpdateRecoveryItemStatus.ValidationFailed,
                BotRecoveryErrorCodes.ValidationError,
                "recoveryStatus is not a recognised value.");
        }

        var oldStatus = item.RecoveryStatus;
        var oldResponsible = item.ResponsibleAdminId;
        var oldNote = item.AdminNote;

        // --- Note ---
        if (request.AdminNote is not null)
        {
            var trimmed = request.AdminNote.Trim();
            if (trimmed.Length > MaxNoteLength)
            {
                return Failure(UpdateRecoveryItemStatus.ValidationFailed,
                    BotRecoveryErrorCodes.ValidationError,
                    $"adminNote must be at most {MaxNoteLength} characters.");
            }
            item.AdminNote = trimmed.Length == 0 ? null : trimmed;
        }

        // --- Responsible admin ---
        if (request.ResponsibleAdminId is { } responsibleId)
        {
            var exists = await _db.Set<User>()
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Id == responsibleId && !u.IsDeleted, cancellationToken);
            if (!exists)
            {
                return Failure(UpdateRecoveryItemStatus.ValidationFailed,
                    BotRecoveryErrorCodes.ResponsibleAdminNotFound,
                    "responsibleAdminId does not reference an existing user.");
            }
            item.ResponsibleAdminId = responsibleId;
        }

        // --- Status transition ---
        if (request.RecoveryStatus is { } newStatus)
        {
            item.RecoveryStatus = newStatus;
            item.ResolvedAt = newStatus == BotRecoveryStatus.RESOLVED
                ? _clock.GetUtcNow().UtcDateTime
                : null;
        }

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.BOT_RECOVERY_UPDATED,
                EntityType: nameof(BotRecoveryItem),
                EntityId: item.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new
                {
                    RecoveryStatus = oldStatus.ToString(),
                    ResponsibleAdminId = oldResponsible,
                    AdminNote = oldNote,
                }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    RecoveryStatus = item.RecoveryStatus.ToString(),
                    ResponsibleAdminId = item.ResponsibleAdminId,
                    AdminNote = item.AdminNote,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var dto = await ProjectItems(
                _db.Set<BotRecoveryItem>().AsNoTracking().Where(r => r.Id == item.Id))
            .FirstOrDefaultAsync(cancellationToken);

        return new UpdateRecoveryItemOutcome(
            UpdateRecoveryItemStatus.Updated, dto, ErrorCode: null, ErrorMessage: null);
    }

    /// <summary>
    /// Join a filtered <see cref="BotRecoveryItem"/> source to its transaction and
    /// the seller / buyer / responsible-admin users (all with query filters
    /// ignored so soft-deleted parties still render) and project the queue DTO.
    /// </summary>
    private IQueryable<BotRecoveryQueueItemDto> ProjectItems(IQueryable<BotRecoveryItem> source)
    {
        return from r in source.IgnoreQueryFilters()
               join t in _db.Set<Transaction>().IgnoreQueryFilters() on r.TransactionId equals t.Id
               join sellerJ in _db.Set<User>().IgnoreQueryFilters()
                   on t.SellerId equals sellerJ.Id into sellerG
               from seller in sellerG.DefaultIfEmpty()
               join buyerJ in _db.Set<User>().IgnoreQueryFilters()
                   on t.BuyerId equals (Guid?)buyerJ.Id into buyerG
               from buyer in buyerG.DefaultIfEmpty()
               join adminJ in _db.Set<User>().IgnoreQueryFilters()
                   on r.ResponsibleAdminId equals (Guid?)adminJ.Id into adminG
               from admin in adminG.DefaultIfEmpty()
               select new BotRecoveryQueueItemDto(
                   r.Id,
                   r.TransactionId,
                   t.ItemName,
                   t.ItemIconUrl,
                   seller != null ? seller.SteamId : string.Empty,
                   seller != null ? seller.SteamDisplayName : null,
                   buyer != null ? buyer.SteamId : null,
                   buyer != null ? buyer.SteamDisplayName : null,
                   t.Status,
                   r.StatusAtRestriction,
                   t.IsOnHold,
                   r.RecoveryStatus,
                   r.ResponsibleAdminId,
                   admin != null ? admin.SteamDisplayName : null,
                   r.AdminNote,
                   r.CreatedAt,
                   r.ResolvedAt);
    }

    private static UpdateRecoveryItemOutcome Failure(
        UpdateRecoveryItemStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
