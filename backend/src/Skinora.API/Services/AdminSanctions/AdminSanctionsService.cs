using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.AdminSanctions;

/// <inheritdoc cref="IAdminSanctionsService"/>
public sealed class AdminSanctionsService : IAdminSanctionsService
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ITrc20AddressValidator _addressValidator;
    private readonly ISanctionsViolationHandler _violations;
    private readonly TimeProvider _clock;

    public AdminSanctionsService(
        AppDbContext db,
        IAuditLogger audit,
        ITrc20AddressValidator addressValidator,
        ISanctionsViolationHandler violations,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _addressValidator = addressValidator;
        _violations = violations;
        _clock = clock;
    }

    public async Task<PagedResult<SanctionedAddressDto>> ListAsync(
        AdminSanctionsListQuery query, CancellationToken cancellationToken)
    {
        var network = string.IsNullOrWhiteSpace(query.Network)
            ? SanctionedAddressNetworks.Trc20
            : query.Network;

        var isActive = query.IsActive ?? true;

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        var q = _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .Where(s => s.Network == network && s.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(query.Source)
            && SanctionedAddressSources.IsKnown(query.Source))
        {
            q = q.Where(s => s.Source == query.Source);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(s => EF.Functions.Like(s.Address, $"%{search}%"));
        }

        var sortBy = (query.SortBy ?? "listedAt").Trim().ToLowerInvariant();
        var desc = !string.Equals(query.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        q = (sortBy, desc) switch
        {
            ("address", true) => q.OrderByDescending(s => s.Address),
            ("address", false) => q.OrderBy(s => s.Address),
            (_, false) => q.OrderBy(s => s.ListedAt),
            _ => q.OrderByDescending(s => s.ListedAt),
        };

        var total = await q.CountAsync(cancellationToken);

        var rows = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var adminIds = rows
            .Where(r => r.AddedByAdminId.HasValue)
            .Select(r => r.AddedByAdminId!.Value)
            .Distinct()
            .ToArray();

        var admins = adminIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.Set<User>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => adminIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.SteamDisplayName })
                .ToDictionaryAsync(x => x.Id, x => string.IsNullOrEmpty(x.Name) ? "—" : x.Name, cancellationToken);

        var items = rows
            .Select(r => MapDto(r, admins))
            .ToList();

        return new PagedResult<SanctionedAddressDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public async Task<AddSanctionedAddressOutcome> AddAsync(
        Guid adminId,
        AddSanctionedAddressRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var address = request.Address?.Trim();
        if (string.IsNullOrWhiteSpace(address))
            return Fail(AddSanctionedAddressStatus.ValidationFailed, "address is required.");

        if (!_addressValidator.IsValid(address))
            return Fail(AddSanctionedAddressStatus.InvalidAddress,
                "Wallet address is not a valid Tron (TRC-20) address.");

        var network = (request.Network ?? SanctionedAddressNetworks.Trc20).Trim();
        if (!SanctionedAddressNetworks.IsKnown(network))
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                $"network must be one of: {string.Join(", ", SanctionedAddressNetworks.All)}.");

        var source = (request.Source ?? SanctionedAddressSources.Manual).Trim();
        if (!SanctionedAddressSources.IsKnown(source))
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                $"source must be one of: {string.Join(", ", SanctionedAddressSources.All)}.");

        var reason = request.Reason?.Trim();
        if (reason is { Length: > 500 })
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                "reason must not exceed 500 characters.");

        // 06 §3.25 filtered UQ — aktif satır başına benzersiz adres. Defansif
        // pre-insert kontrolü: race race ihtimaline karşı DB constraint son
        // hat olarak kalır; concurrent insert UQ ihlali ile düşer (catch).
        var exists = await _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .AnyAsync(s => s.IsActive && s.Address == address, cancellationToken);

        if (exists)
            return Fail(AddSanctionedAddressStatus.AlreadyListed,
                "Address is already on the active sanctions list.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var entity = new SanctionedAddress
        {
            Id = Guid.NewGuid(),
            Address = address,
            Network = network,
            Source = source,
            Reason = reason,
            ListedAt = nowUtc,
            AddedByAdminId = adminId,
            IsActive = true,
        };

        _db.Set<SanctionedAddress>().Add(entity);

        await _audit.LogAsync(new AuditLogEntry(
            UserId: null,
            ActorId: adminId,
            ActorType: ActorType.ADMIN,
            Action: AuditAction.SANCTIONS_LIST_ADDRESS_ADDED,
            EntityType: nameof(SanctionedAddress),
            EntityId: entity.Id.ToString(),
            OldValue: null,
            NewValue: JsonSerializer.Serialize(new
            {
                address = entity.Address,
                network = entity.Network,
                source = entity.Source,
                reason = entity.Reason,
            }),
            IpAddress: ipAddress), cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent admin AD23 — UQ_SanctionedAddresses_Address_Active
            // tetiklendi. Diğer admin'in girdiği satır aktifse 409 döndür;
            // değilse rethrow (await catch filter'ında yasak, bu yüzden
            // body içinde check yapılır).
            if (await IsAddressNowActiveAsync(address, cancellationToken))
            {
                return Fail(AddSanctionedAddressStatus.AlreadyListed,
                    "Address is already on the active sanctions list.");
            }

            throw;
        }

        // 06 §3.25 retroaktif eşleşme: yeni adres mevcut kullanıcıların
        // DefaultPayoutAddress / DefaultRefundAddress'i ile eşleşirse fraud
        // flag + emergency hold cascade tetikle (07 §9.24 "Yan etkiler").
        var affectedUserIds = await _db.Set<User>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u =>
                u.DefaultPayoutAddress == address
                || u.DefaultRefundAddress == address)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in affectedUserIds)
        {
            await _violations.RecordRetroactiveMatchAsync(
                userId, address, source, cancellationToken);
        }

        var dto = MapDto(entity, new Dictionary<Guid, string>());
        if (entity.AddedByAdminId.HasValue)
        {
            var adminName = await _db.Set<User>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.Id == entity.AddedByAdminId.Value)
                .Select(u => u.SteamDisplayName)
                .FirstOrDefaultAsync(cancellationToken);
            dto = dto with
            {
                AddedBy = new SanctionedAddressAdminDto(
                    entity.AddedByAdminId.Value,
                    string.IsNullOrEmpty(adminName) ? "—" : adminName),
            };
        }

        return new AddSanctionedAddressOutcome(
            AddSanctionedAddressStatus.Added, dto, null);
    }

    public async Task<DeactivateSanctionedAddressOutcome> DeactivateAsync(
        Guid adminId,
        Guid sanctionedAddressId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var row = await _db.Set<SanctionedAddress>()
            .FirstOrDefaultAsync(s => s.Id == sanctionedAddressId, cancellationToken);

        if (row is null)
            return new DeactivateSanctionedAddressOutcome(
                DeactivateSanctionedAddressStatus.NotFound, null,
                $"Sanctioned address '{sanctionedAddressId}' was not found.");

        if (!row.IsActive)
            return new DeactivateSanctionedAddressOutcome(
                DeactivateSanctionedAddressStatus.AlreadyInactive, null,
                "Sanctioned address is already inactive.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        row.IsActive = false;

        await _audit.LogAsync(new AuditLogEntry(
            UserId: null,
            ActorId: adminId,
            ActorType: ActorType.ADMIN,
            Action: AuditAction.SANCTIONS_LIST_ADDRESS_REMOVED,
            EntityType: nameof(SanctionedAddress),
            EntityId: row.Id.ToString(),
            OldValue: null,
            NewValue: JsonSerializer.Serialize(new
            {
                address = row.Address,
                source = row.Source,
            }),
            IpAddress: ipAddress), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new DeactivateSanctionedAddressOutcome(
            DeactivateSanctionedAddressStatus.Deactivated,
            new DeactivateSanctionedAddressResponse(
                row.Id, row.Address, false, nowUtc),
            null);
    }

    private async Task<bool> IsAddressNowActiveAsync(
        string address, CancellationToken cancellationToken)
        => await _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .AnyAsync(s => s.IsActive && s.Address == address, cancellationToken);

    private static AddSanctionedAddressOutcome Fail(
        AddSanctionedAddressStatus status, string message)
        => new(status, null, message);

    private static SanctionedAddressDto MapDto(
        SanctionedAddress row, IReadOnlyDictionary<Guid, string> admins)
    {
        SanctionedAddressAdminDto? addedBy = null;
        if (row.AddedByAdminId.HasValue
            && admins.TryGetValue(row.AddedByAdminId.Value, out var name))
        {
            addedBy = new SanctionedAddressAdminDto(row.AddedByAdminId.Value, name);
        }

        return new SanctionedAddressDto(
            Id: row.Id,
            Address: row.Address,
            Network: row.Network,
            Source: row.Source,
            Reason: row.Reason,
            ListedAt: row.ListedAt,
            AddedBy: addedBy,
            IsActive: row.IsActive,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt);
    }
}
