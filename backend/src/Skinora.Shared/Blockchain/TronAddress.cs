using System.Numerics;
using System.Security.Cryptography;

namespace Skinora.Shared.Blockchain;

/// <summary>
/// TRON address rules (02 §12.3, 07 §1.3, 08 §3.0 "Adres biçimi"): a TRON
/// address is the base58check encoding of 21 bytes — the version byte
/// <c>0x41</c> followed by the 20-byte account id — plus a 4-byte checksum,
/// the first four bytes of <c>SHA-256(SHA-256(21 bytes))</c>. That always
/// renders as 34 characters starting with <c>T</c>.
/// </summary>
/// <remarks>
/// <para>
/// The checksum is what makes a typo detectable: a regex over the alphabet,
/// the length and the <c>T</c> prefix (the pre-T146 validator) accepts a
/// single mistyped character with probability ~1, the checksum with
/// probability ~2<sup>-32</sup>. The version byte is checked too — the same
/// payload base58check-encoded with version <c>0x40</c> or <c>0x42</c> is
/// still 34 characters starting with <c>T</c>, has a correct checksum, and is
/// not a TRON address.
/// </para>
/// <para>
/// Base58 is case-significant, so the value is never case-folded (06 §1.4):
/// the lowercased spelling of a valid address is a different string and fails
/// the checksum.
/// </para>
/// </remarks>
public static class TronAddress
{
    /// <summary>Encoded length of every TRON address.</summary>
    public const int Length = 34;

    private const byte VersionByte = 0x41;
    private const int PayloadBytes = 21;
    private const int ChecksumBytes = 4;

    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>
    /// <c>true</c> when <paramref name="value"/> is a well-formed TRON address
    /// with a valid base58check checksum and the <c>0x41</c> version byte. No
    /// trimming — surrounding whitespace makes the value invalid.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length != Length || value[0] != 'T')
            return false;

        if (!TryDecodeBase58(value, out var decoded) || decoded.Length != PayloadBytes + ChecksumBytes)
            return false;

        if (decoded[0] != VersionByte)
            return false;

        var payload = decoded.AsSpan(0, PayloadBytes);
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        SHA256.HashData(payload, first);
        SHA256.HashData(first, second);

        return second[..ChecksumBytes].SequenceEqual(decoded.AsSpan(PayloadBytes, ChecksumBytes));
    }

    private static bool TryDecodeBase58(string value, out byte[] bytes)
    {
        bytes = [];
        BigInteger number = BigInteger.Zero;
        foreach (var c in value)
        {
            var digit = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
                return false;
            number = (number * 58) + digit;
        }

        // Leading '1' characters encode leading zero bytes.
        var leadingZeros = 0;
        while (leadingZeros < value.Length && value[leadingZeros] == '1')
            leadingZeros++;

        var body = number.IsZero
            ? []
            : number.ToByteArray(isUnsigned: true, isBigEndian: true);

        bytes = new byte[leadingZeros + body.Length];
        body.CopyTo(bytes, leadingZeros);
        return true;
    }
}
