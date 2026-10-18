using System.Text;

namespace Skinora.Shared.Blockchain;

/// <summary>Outcome of <see cref="EvmAddress.Check"/>.</summary>
public enum EvmAddressCheck
{
    /// <summary>Not <c>0x</c> + 40 hex digits, or mixed case with a wrong EIP-55 checksum.</summary>
    Invalid,

    /// <summary>Mixed case and the EIP-55 checksum holds — typo-protected.</summary>
    Checksummed,

    /// <summary>
    /// All-lowercase or all-uppercase hex. Well-formed, but carries no checksum,
    /// so a typo cannot be detected (EIP-55). Whether that is acceptable is the
    /// caller's decision (T146 asks the user for a second confirmation).
    /// </summary>
    NotChecksummed,
}

/// <summary>
/// Format and EIP-55 checksum rules for an EVM address (06 §1.4, 07 §1.3,
/// 08 §3.0 "Adres biçimi"). Storage and comparison use the lowercase canonical
/// form (<see cref="BlockchainAddress"/>); EIP-55 is the display form and the
/// typo check on input.
/// </summary>
public static class EvmAddress
{
    private const int HexLength = 40;

    /// <summary>Classify <paramref name="value"/>; see <see cref="EvmAddressCheck"/>.</summary>
    public static EvmAddressCheck Check(string? value)
    {
        if (!IsWellFormed(value))
            return EvmAddressCheck.Invalid;

        var hex = value!.AsSpan(2);
        var hasLower = false;
        var hasUpper = false;
        foreach (var c in hex)
        {
            if (c is >= 'a' and <= 'f') hasLower = true;
            else if (c is >= 'A' and <= 'F') hasUpper = true;
        }

        if (!(hasLower && hasUpper))
            return EvmAddressCheck.NotChecksummed;

        return string.Equals(value, ToChecksum(value), StringComparison.Ordinal)
            ? EvmAddressCheck.Checksummed
            : EvmAddressCheck.Invalid;
    }

    /// <summary>
    /// <c>true</c> for exactly <c>0x</c> + 40 hex digits (either letter case).
    /// The prefix must be a lowercase <c>0x</c>.
    /// </summary>
    public static bool IsWellFormed(string? value)
    {
        if (value is null || value.Length != HexLength + 2 || value[0] != '0' || value[1] != 'x')
            return false;

        for (var i = 2; i < value.Length; i++)
        {
            if (!char.IsAsciiHexDigit(value[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// EIP-55 mixed-case form of a well-formed address: every hex letter is
    /// uppercased where the matching nibble of
    /// <c>keccak256(lowercase hex, as ASCII)</c> is 8 or more.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not <c>0x</c> + 40 hex digits.</exception>
    public static string ToChecksum(string value)
    {
        if (!IsWellFormed(value))
            throw new ArgumentException("Not an EVM address (0x + 40 hex digits).", nameof(value));

        var lower = value[2..].ToLowerInvariant();
        var hash = Keccak256.Hash(Encoding.ASCII.GetBytes(lower));

        var result = new StringBuilder("0x", HexLength + 2);
        for (var i = 0; i < HexLength; i++)
        {
            var c = lower[i];
            var nibble = (i % 2 == 0) ? hash[i / 2] >> 4 : hash[i / 2] & 0x0F;
            result.Append(c is >= 'a' and <= 'f' && nibble >= 8 ? char.ToUpperInvariant(c) : c);
        }

        return result.ToString();
    }
}
