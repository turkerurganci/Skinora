using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// EF Core impl of <see cref="ISanctionedAddressLookup"/> — <c>AsNoTracking</c>
/// lookup keyed on <c>(Network, kanonik Address)</c> (06 §1.4, §3.25 filtered UQ
/// <c>UQ_SanctionedAddresses_Network_Address_Active</c>). Yalnız
/// <c>IsActive = true</c> satır döner.
/// </summary>
/// <remarks>
/// The SQL predicate narrows by index; the final decision is an ordinal
/// comparison of canonical forms in memory. That keeps the match semantics
/// independent of the database collation: SQL Server's default collation is
/// case-insensitive, which would let a TRON address that differs only in
/// letter case — a different base58 address — match a listed one. EVM
/// addresses are stored and passed in lowercase, so they match whatever case
/// the caller typed.
/// </remarks>
public sealed class SanctionedAddressLookup : ISanctionedAddressLookup
{
    private readonly AppDbContext _db;

    public SanctionedAddressLookup(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SanctionedAddressMatch?> FindActiveAsync(
        BlockchainNetwork network, string address, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var canonical = BlockchainAddress.Canonicalize(address);

        var candidates = await _db.Set<SanctionedAddress>()
            .AsNoTracking()
            .Where(s => s.IsActive && s.Network == network && s.Address == canonical)
            .Select(s => new SanctionedAddressMatch(
                s.Id, s.Address, s.Network, s.Source, s.ListedAt))
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(
            c => string.Equals(c.Address, canonical, StringComparison.Ordinal));
    }
}
