using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;

namespace Skinora.Steam.Application.BotSelection;

/// <inheritdoc cref="IBotSelectionService"/>
public sealed class SqlBotSelectionService : IBotSelectionService
{
    private readonly AppDbContext _db;

    public SqlBotSelectionService(AppDbContext db)
    {
        _db = db;
    }

    public Task<PlatformSteamBot?> SelectAsync(CancellationToken cancellationToken)
    {
        // The PlatformSteamBot soft-delete query filter (configured in
        // PlatformSteamBotConfiguration) already excludes IsDeleted == true,
        // so the where-clause below only needs the ACTIVE status filter.
        // AsNoTracking: the caller does not mutate the returned row inside
        // the same DbContext — it forwards the DisplayName to the sidecar and
        // re-fetches with tracking only when persisting the EscrowBotId on
        // the Transaction (forward-deferred caller).
        return _db.Set<PlatformSteamBot>()
            .AsNoTracking()
            .Where(b => b.Status == PlatformSteamBotStatus.ACTIVE)
            .OrderBy(b => b.ActiveEscrowCount)
            .ThenBy(b => b.LastHealthCheckAt ?? DateTime.MinValue)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
