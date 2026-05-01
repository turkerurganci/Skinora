using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Platform.Application.Audit;

/// <inheritdoc cref="IAuditLogQueryService"/>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const string SystemDisplayName = "System";
    private const string TransactionEntityType = "Transaction";

    private readonly AppDbContext _db;

    public AuditLogQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogListItemDto>> ListAsync(
        AuditLogListQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        var dbQuery = _db.Set<AuditLog>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // Unknown category => empty result set (no 400 — 07 §9.19 lists three
            // values; clients that send unknown values get nothing rather than
            // a hard error, mirroring how AD15 handles unknown roleId).
            var actions = AuditLogCategoryMap.ActionsInCategory(query.Category);
            if (actions.Count == 0)
            {
                return new PagedResult<AuditLogListItemDto>
                {
                    Items = Array.Empty<AuditLogListItemDto>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize,
                };
            }
            dbQuery = dbQuery.Where(a => actions.Contains(a.Action));
        }

        if (query.DateFrom.HasValue)
            dbQuery = dbQuery.Where(a => a.CreatedAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            dbQuery = dbQuery.Where(a => a.CreatedAt <= query.DateTo.Value);

        if (query.TransactionId.HasValue)
        {
            var txId = query.TransactionId.Value.ToString();
            dbQuery = dbQuery.Where(a =>
                a.EntityType == TransactionEntityType && a.EntityId == txId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EntityId is the most operator-useful free-text scope — settings
            // keys, transaction ids, user ids all surface there. Action enum
            // is also matched as string because the column is stored as text
            // (T17 EnumToStringConverter).
            var searchTerm = query.Search.Trim();
            dbQuery = dbQuery.Where(a =>
                EF.Functions.Like(a.EntityId, $"%{searchTerm}%"));
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var rows = await dbQuery
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Hydrate actor + subject in a single round-trip — collect every Guid
        // referenced by the rows and join the Users table once.
        var userIds = rows
            .Select(r => r.ActorId)
            .Concat(rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value))
            .Distinct()
            .ToList();

        var users = userIds.Count == 0
            ? new Dictionary<Guid, (string SteamId, string DisplayName)>()
            : await _db.Set<User>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.SteamId, u.SteamDisplayName })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => (u.SteamId, u.SteamDisplayName),
                    cancellationToken);

        var items = rows
            .Select(row => MapRow(row, users))
            .ToList();

        return new PagedResult<AuditLogListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static AuditLogListItemDto MapRow(
        AuditLog row,
        IReadOnlyDictionary<Guid, (string SteamId, string DisplayName)> users)
    {
        var actor = ResolveParticipant(row.ActorId, row.ActorType, users);

        AuditLogParticipantDto? subject = null;
        if (row.UserId.HasValue && row.UserId.Value != row.ActorId)
        {
            // Subject only when the affected user differs from the actor — when
            // the actor and subject are the same the field is redundant noise.
            var subjectActorType = row.UserId.Value == SeedConstants.SystemUserId
                ? ActorType.SYSTEM
                : ActorType.USER;
            subject = ResolveParticipant(row.UserId.Value, subjectActorType, users);
        }

        Guid? transactionId = null;
        if (row.EntityType == TransactionEntityType
            && Guid.TryParse(row.EntityId, out var parsedTx))
        {
            transactionId = parsedTx;
        }

        return new AuditLogListItemDto(
            Id: row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Category: AuditLogCategoryMap.CategoryFor(row.Action),
            Action: row.Action.ToString(),
            Actor: actor,
            Subject: subject,
            TransactionId: transactionId,
            Detail: TryParseJson(row.NewValue),
            CreatedAt: row.CreatedAt);
    }

    private static AuditLogParticipantDto ResolveParticipant(
        Guid id,
        ActorType type,
        IReadOnlyDictionary<Guid, (string SteamId, string DisplayName)> users)
    {
        if (type == ActorType.SYSTEM || id == SeedConstants.SystemUserId)
            return new AuditLogParticipantDto(SteamId: null, DisplayName: SystemDisplayName);

        if (users.TryGetValue(id, out var user))
            return new AuditLogParticipantDto(user.SteamId, user.DisplayName);

        // User row missing (deleted hard from DB, FK not enforced) — surface a
        // best-effort placeholder so the response stays well-formed.
        return new AuditLogParticipantDto(
            SteamId: null,
            DisplayName: $"unknown:{id}");
    }

    private static JsonElement? TryParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Non-JSON OldValue/NewValue is allowed (06 §3.20: JSON encoding is
            // recommended, not required). Surface raw text wrapped as a string
            // node so the API stays uniform.
            return JsonDocument.Parse(JsonSerializer.Serialize(raw)).RootElement.Clone();
        }
    }
}
