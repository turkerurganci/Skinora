using Skinora.Shared.Enums;

namespace Skinora.Shared.Sanctions;

/// <summary>
/// Result of a successful sanctions list lookup (06 §3.25). Surfaced to
/// <see cref="ISanctionedAddressLookup.FindActiveAsync"/> callers — wallet
/// + login sanctions checks ve admin retroaktif scan.
/// </summary>
/// <param name="Id">SanctionedAddress.Id.</param>
/// <param name="Address">Matched address, in canonical form (06 §1.4).</param>
/// <param name="Network">Network of the listed address (06 §2.25).</param>
/// <param name="Source">List source — <c>OFAC</c> / <c>EU</c> / <c>UN</c> / <c>MANUAL</c>.</param>
/// <param name="ListedAt">Original listing date.</param>
public sealed record SanctionedAddressMatch(
    Guid Id,
    string Address,
    BlockchainNetwork Network,
    string Source,
    DateTime ListedAt);
