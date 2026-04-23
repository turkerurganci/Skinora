namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Format-level validator for Tron TRC-20 addresses — 02 §12.3, 03 §9
/// "Merkezi Cüzdan Adresi Doğrulama Kuralı" step (1).
/// Sanctions screening (step 2) lives in <see cref="IWalletSanctionsCheck"/>.
/// </summary>
public interface ITrc20AddressValidator
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="address"/> matches the TRC-20
    /// shape: starts with <c>T</c>, exactly 34 characters, every character in
    /// the Base58 alphabet (no <c>0 O I l</c>). The full Base58Check checksum
    /// is not verified in MVP — 07 §5.3 defines format-only. Null/whitespace
    /// input returns <c>false</c>.
    /// </summary>
    bool IsValid(string? address);
}

public sealed class Trc20AddressValidator : ITrc20AddressValidator
{
    private const int Trc20Length = 34;
    private const char TronPrefix = 'T';

    // Base58 alphabet — Bitcoin/Tron variant. Excludes 0, O, I, l to avoid
    // visual ambiguity. A TRC-20 address is Base58Check-encoded; MVP only
    // validates the character set + length + prefix per 07 §5.3.
    private const string Base58Alphabet =
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private static readonly HashSet<char> AllowedChars = new(Base58Alphabet);

    public bool IsValid(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        if (address.Length != Trc20Length) return false;
        if (address[0] != TronPrefix) return false;

        for (var i = 0; i < address.Length; i++)
        {
            if (!AllowedChars.Contains(address[i])) return false;
        }

        return true;
    }
}
