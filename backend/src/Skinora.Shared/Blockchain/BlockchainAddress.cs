namespace Skinora.Shared.Blockchain;

/// <summary>
/// The single canonical form of an on-chain identifier — address, contract
/// address or transaction hash — and the only comparison the code base should
/// use on one (06 §1.4 "Kanonik adres biçimi").
/// </summary>
/// <remarks>
/// <para>
/// The rule is decided by the value's shape, never by a network argument,
/// because the two shapes cannot be confused:
/// </para>
/// <list type="bullet">
///   <item><description><b>EVM</b> (<c>0x</c> followed by hex digits — an
///   address, a contract or a transaction hash): hex is case-insensitive, and
///   EIP-55 uses the letter case only as a checksum. The canonical form is
///   all lowercase. Two spellings of the same address compare equal.</description></item>
///   <item><description><b>Anything else</b> (TRON base58check, a TRON
///   transaction id): returned <b>unchanged</b>. Base58 is case-significant —
///   lowercasing a TRON address produces a different, usually invalid,
///   address — so this helper never touches its case. Base58 has no
///   <c>0</c> digit, so a TRON address can never start with <c>0x</c>.</description></item>
/// </list>
/// <para>
/// Storage goes through <c>CanonicalChainIdentifierConverter</c>, which applies
/// <see cref="Canonicalize(string)"/> on every write, so SQL comparisons against
/// a converted column receive a canonical parameter as well. In-memory
/// comparisons (a configured hot wallet against a balance snapshot, a contract
/// against an allowlist) must use <see cref="AreEqual"/> or
/// <see cref="Comparer"/> — a plain <c>==</c> or <c>StringComparison.Ordinal</c>
/// misses an EVM value spelled in a different case.
/// </para>
/// </remarks>
public static class BlockchainAddress
{
    /// <summary>
    /// Returns the canonical form: an <c>0x</c>-prefixed hex value in lowercase,
    /// anything else unchanged. No trimming — input hygiene is the validator's job.
    /// </summary>
    public static string Canonicalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsZeroXHex(value) ? value.ToLowerInvariant() : value;
    }

    /// <summary><see cref="Canonicalize(string)"/> that passes <c>null</c> through.</summary>
    public static string? CanonicalizeOrNull(string? value)
        => value is null ? null : Canonicalize(value);

    /// <summary>
    /// Canonical equality: EVM values compare case-insensitively, TRON values
    /// exactly. Two <c>null</c>s are not considered equal — an absent address
    /// never "matches" anything.
    /// </summary>
    public static bool AreEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return string.Equals(Canonicalize(a), Canonicalize(b), StringComparison.Ordinal);
    }

    /// <summary>Equality comparer over the canonical form, for sets and dictionaries.</summary>
    public static IEqualityComparer<string> Comparer { get; } = new CanonicalComparer();

    /// <summary>
    /// <c>true</c> for <c>0x</c> (either case of the <c>x</c>) followed by at
    /// least one hex digit and nothing else.
    /// </summary>
    public static bool IsZeroXHex(string value)
    {
        if (value.Length < 3 || value[0] != '0' || (value[1] != 'x' && value[1] != 'X'))
            return false;

        for (var i = 2; i < value.Length; i++)
        {
            if (!char.IsAsciiHexDigit(value[i]))
                return false;
        }

        return true;
    }

    private sealed class CanonicalComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (x is null && y is null) return true;
            return AreEqual(x, y);
        }

        public int GetHashCode(string obj)
            => StringComparer.Ordinal.GetHashCode(Canonicalize(obj));
    }
}
