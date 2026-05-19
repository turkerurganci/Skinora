using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core impl of <see cref="ISanctionedAddressLookup"/> — single-row
/// <c>AsNoTracking</c> lookup keyed on the case-sensitive address
/// (06 §3.25 filtered UQ <c>UQ_SanctionedAddresses_Address_Active</c>).
/// Yalnız <c>IsActive = true</c> satır döner.
/// </summary>
public sealed class SanctionedAddressLookup : ISanctionedAddressLookup
{
    private readonly AppDbContext _db;

    public SanctionedAddressLookup(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SanctionedAddressMatch?> FindActiveAsync(
        string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var row = await _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .Where(s => s.IsActive && s.Address == address)
            .Select(s => new SanctionedAddressMatch(
                s.Id, s.Address, s.Network, s.Source, s.ListedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }
}
