using Skinora.Shared.Enums;

namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Wire values accepted for <see cref="SanctionedAddress.Network"/> on the
/// admin API (07 §9.23–§9.24): the <see cref="BlockchainNetwork"/> names,
/// exactly as stored — <c>TRON</c>, <c>ETHEREUM</c>. The pre-F8 value
/// <c>'TRC-20'</c> is no longer accepted (T145 migrated stored rows to
/// <c>TRON</c>).
/// </summary>
public static class SanctionedAddressNetworks
{
    public static readonly IReadOnlyList<string> All =
        Enum.GetNames<BlockchainNetwork>();

    /// <summary>
    /// Parses a wire value. Case-sensitive on purpose — the value is an enum
    /// name, and accepting <c>"tron"</c> would let two spellings of the same
    /// network reach audit payloads.
    /// </summary>
    public static bool TryParse(string? value, out BlockchainNetwork network)
    {
        network = default;
        if (string.IsNullOrEmpty(value) || !All.Contains(value, StringComparer.Ordinal))
            return false;

        network = Enum.Parse<BlockchainNetwork>(value);
        return true;
    }
}
