using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Format step (1) of the central wallet-address pipeline (02 §12.3, 03 §9
/// "Merkezi Cüzdan Adresi Doğrulama Kuralı", 07 §1.3) — the validator is
/// chosen by network. Sanctions screening, step (2), lives in
/// <see cref="IWalletSanctionsCheck"/>.
/// </summary>
public interface IWalletAddressValidator
{
    /// <summary>
    /// Classifies <paramref name="address"/> for <paramref name="network"/>.
    /// No trimming — surrounding whitespace makes the value invalid.
    /// </summary>
    WalletAddressValidation Validate(BlockchainNetwork network, string? address);
}

/// <summary>Outcome of <see cref="IWalletAddressValidator.Validate"/>.</summary>
public enum WalletAddressValidationStatus
{
    /// <summary>
    /// Well-formed and typo-protected: a TRON address whose base58check
    /// checksum holds, or a mixed-case EVM address whose EIP-55 checksum holds.
    /// </summary>
    Valid,

    /// <summary>
    /// An EVM address written all-lowercase or all-uppercase (07 §1.3). It is
    /// well-formed but carries no EIP-55 checksum, so a typo cannot be
    /// detected; the caller asks the user for a second confirmation and only
    /// then accepts it.
    /// </summary>
    ValidWithoutChecksum,

    /// <summary>Wrong shape for the network (length, prefix, alphabet, version byte).</summary>
    InvalidFormat,

    /// <summary>Right shape, wrong checksum: TRON base58check or EVM mixed-case EIP-55.</summary>
    ChecksumMismatch,
}

/// <param name="Status">See <see cref="WalletAddressValidationStatus"/>.</param>
/// <param name="CanonicalAddress">
/// The stored and compared form (06 §1.4) when the address is usable
/// (<see cref="WalletAddressValidationStatus.Valid"/> or
/// <see cref="WalletAddressValidationStatus.ValidWithoutChecksum"/>);
/// <c>null</c> otherwise.
/// </param>
public sealed record WalletAddressValidation(
    WalletAddressValidationStatus Status,
    string? CanonicalAddress)
{
    /// <summary><c>true</c> when the address may be stored (possibly after a confirmation).</summary>
    public bool IsUsable => CanonicalAddress is not null;

    /// <summary>
    /// Wire value of <c>error.details.reason</c> on 400 <c>INVALID_WALLET_ADDRESS</c>
    /// (07 §5.3) — tells the client whether to show "invalid" or ask for confirmation.
    /// </summary>
    public string? Reason => Status switch
    {
        WalletAddressValidationStatus.InvalidFormat => WalletAddressRejectionReasons.InvalidFormat,
        WalletAddressValidationStatus.ChecksumMismatch => WalletAddressRejectionReasons.ChecksumMismatch,
        WalletAddressValidationStatus.ValidWithoutChecksum => WalletAddressRejectionReasons.ChecksumConfirmationRequired,
        _ => null,
    };
}

/// <summary>Stable <c>error.details.reason</c> values for 400 <c>INVALID_WALLET_ADDRESS</c> (07 §5.3).</summary>
public static class WalletAddressRejectionReasons
{
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string ChecksumMismatch = "CHECKSUM_MISMATCH";
    public const string ChecksumConfirmationRequired = "CHECKSUM_CONFIRMATION_REQUIRED";
}

/// <inheritdoc cref="IWalletAddressValidator"/>
public sealed class WalletAddressValidator : IWalletAddressValidator
{
    public WalletAddressValidation Validate(BlockchainNetwork network, string? address)
        => network switch
        {
            BlockchainNetwork.TRON => ValidateTron(address),
            BlockchainNetwork.ETHEREUM => ValidateEvm(address),
            _ => new WalletAddressValidation(WalletAddressValidationStatus.InvalidFormat, null),
        };

    private static WalletAddressValidation ValidateTron(string? address)
    {
        if (TronAddress.IsValid(address))
            return new WalletAddressValidation(WalletAddressValidationStatus.Valid, address);

        // Distinguish "looks like a TRON address but the checksum (or version
        // byte) is wrong" from "not even the shape" so the client can say which.
        return new WalletAddressValidation(
            LooksLikeTron(address)
                ? WalletAddressValidationStatus.ChecksumMismatch
                : WalletAddressValidationStatus.InvalidFormat,
            null);
    }

    private static WalletAddressValidation ValidateEvm(string? address)
        => EvmAddress.Check(address) switch
        {
            EvmAddressCheck.Checksummed => new WalletAddressValidation(
                WalletAddressValidationStatus.Valid, BlockchainAddress.Canonicalize(address!)),
            EvmAddressCheck.NotChecksummed => new WalletAddressValidation(
                WalletAddressValidationStatus.ValidWithoutChecksum, BlockchainAddress.Canonicalize(address!)),
            _ => new WalletAddressValidation(
                EvmAddress.IsWellFormed(address)
                    ? WalletAddressValidationStatus.ChecksumMismatch
                    : WalletAddressValidationStatus.InvalidFormat,
                null),
        };

    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private static bool LooksLikeTron(string? address)
        => address is { Length: TronAddress.Length }
           && address[0] == 'T'
           && address.All(c => Base58Alphabet.Contains(c, StringComparison.Ordinal));
}
