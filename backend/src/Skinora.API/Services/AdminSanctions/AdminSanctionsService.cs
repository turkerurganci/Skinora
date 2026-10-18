using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Blockchain;
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
        var isActive = query.IsActive ?? true;

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => query.PageSize,
        };

        // 07 §9.23 — `network` filtresi BlockchainNetwork adıdır (T145);
        // verilmezse TRON. Tanınmayan bir değer (ör. T145 öncesinin 'TRC-20')
        // hiçbir satırla eşleşmez — eski davranışla aynı: bilinmeyen filtre
        // boş sayfa döner, 400 değil.
        if (!string.IsNullOrWhiteSpace(query.Network)
            && !SanctionedAddressNetworks.TryParse(query.Network, out _))
        {
            return new PagedResult<SanctionedAddressDto>
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
            };
        }

        var network = string.IsNullOrWhiteSpace(query.Network)
            ? BlockchainNetwork.TRON
            : Enum.Parse<BlockchainNetwork>(query.Network);

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
            var pattern = $"%{SqlLikeEscaper.Escape(search)}%";
            q = q.Where(s => EF.Functions.Like(s.Address, pattern));
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
        var rawAddress = request.Address?.Trim();
        if (string.IsNullOrWhiteSpace(rawAddress))
            return Fail(AddSanctionedAddressStatus.ValidationFailed, "address is required.");

        // 07 §9.24 (T145) — network is a BlockchainNetwork name; omitted means
        // TRON (pre-F8 behaviour). The validator is chosen by network.
        var networkValue = string.IsNullOrWhiteSpace(request.Network)
            ? nameof(BlockchainNetwork.TRON)
            : request.Network.Trim();
        if (!SanctionedAddressNetworks.TryParse(networkValue, out var network))
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                $"network must be one of: {string.Join(", ", SanctionedAddressNetworks.All)}.");

        switch (network)
        {
            case BlockchainNetwork.TRON when !_addressValidator.IsValid(rawAddress):
                return Fail(AddSanctionedAddressStatus.InvalidAddress,
                    "Wallet address is not a valid Tron (TRC-20) address.");
            // EIP-55: a mixed-case address must carry a valid checksum. An
            // all-lowercase / all-uppercase address has no checksum to verify
            // and is accepted — the list is curated by an admin copying from
            // the source feed, and the stored form is lowercase either way
            // (06 §1.4).
            case BlockchainNetwork.ETHEREUM when EvmAddress.Check(rawAddress) == EvmAddressCheck.Invalid:
                return Fail(AddSanctionedAddressStatus.InvalidAddress,
                    "Wallet address is not a valid Ethereum address (0x + 40 hex, EIP-55 checksum when mixed case).");
        }

        // Canonical form (06 §1.4): what is stored, compared and echoed back.
        var address = BlockchainAddress.Canonicalize(rawAddress);

        var source = (request.Source ?? SanctionedAddressSources.Manual).Trim();
        if (!SanctionedAddressSources.IsKnown(source))
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                $"source must be one of: {string.Join(", ", SanctionedAddressSources.All)}.");

        var reason = request.Reason?.Trim();
        if (reason is { Length: > 500 })
            return Fail(AddSanctionedAddressStatus.ValidationFailed,
                "reason must not exceed 500 characters.");

        // 06 §3.25 filtered UQ — ağ başına aktif satır başına benzersiz adres.
        // Defansif pre-insert kontrolü: race ihtimaline karşı DB constraint
        // son hat olarak kalır; concurrent insert UQ ihlali ile düşer (catch).
        var exists = await IsAddressNowActiveAsync(network, address, cancellationToken);

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
                network = entity.Network.ToString(),
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
            // Concurrent admin AD23 — UQ_SanctionedAddresses_Network_Address_Active
            // tetiklendi. Diğer admin'in girdiği satır aktifse 409 döndür;
            // değilse rethrow (await catch filter'ında yasak, bu yüzden
            // body içinde check yapılır).
            if (await IsAddressNowActiveAsync(network, address, cancellationToken))
            {
                return Fail(AddSanctionedAddressStatus.AlreadyListed,
                    "Address is already on the active sanctions list.");
            }

            throw;
        }

        // 06 §3.25 retroaktif eşleşme: yeni adres mevcut kullanıcıların
        // DefaultPayoutAddress / DefaultRefundAddress'i ile eşleşirse fraud
        // flag + emergency hold cascade tetikle (07 §9.24 "Yan etkiler").
        // Profil adresleri T146'ya kadar TRON adresidir (06 §1.4); başka bir
        // ağın listesi bugün hiçbir profil adresiyle eşleşemez. Son karar
        // kanonik biçimin birebir karşılaştırmasıdır — DB collation'ı
        // büyük/küçük harfe duyarsız olsa da TRON base58 duyarlı kalır.
        var affectedUserIds = new List<Guid>();
        if (network == BlockchainNetwork.TRON)
        {
            var candidates = await _db.Set<User>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u =>
                    u.DefaultPayoutAddress == address
                    || u.DefaultRefundAddress == address)
                .Select(u => new { u.Id, u.DefaultPayoutAddress, u.DefaultRefundAddress })
                .ToListAsync(cancellationToken);

            affectedUserIds.AddRange(candidates
                .Where(u => string.Equals(u.DefaultPayoutAddress, address, StringComparison.Ordinal)
                            || string.Equals(u.DefaultRefundAddress, address, StringComparison.Ordinal))
                .Select(u => u.Id));
        }

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
                network = row.Network.ToString(),
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

    // `address` is canonical. The UQ is evaluated by the database collation,
    // so the pre-check asks the same question the index does.
    private async Task<bool> IsAddressNowActiveAsync(
        BlockchainNetwork network, string address, CancellationToken cancellationToken)
        => await _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .AnyAsync(s => s.IsActive && s.Network == network && s.Address == address, cancellationToken);

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
            Network: row.Network.ToString(),
            Source: row.Source,
            Reason: row.Reason,
            ListedAt: row.ListedAt,
            AddedBy: addedBy,
            IsActive: row.IsActive,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt);
    }
}
