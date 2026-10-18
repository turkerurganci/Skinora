using Skinora.Shared.Enums;
using Skinora.Users.Application.Wallet;

namespace Skinora.Users.Tests.Unit.Wallet;

/// <summary>
/// T146 — the per-network validator of the central wallet pipeline (02 §12.3,
/// 07 §1.3). Expected verdicts are independent: TRON from tronweb 5.3.5
/// <c>isAddress</c>, Ethereum from the EIP-55 specification test vectors
/// (eips.ethereum.org/EIPS/eip-55) and ethers v6.17.0 <c>getAddress</c>.
/// </summary>
[Trait("Category", "Unit")]
public class WalletAddressValidatorTests
{
    private readonly WalletAddressValidator _sut = new();

    // ---------- TRON ----------

    [Theory]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]
    [InlineData("TWHPPkNRxo1ovxC2uCWmyoir9AiWpt4F2H")]
    public void Tron_Valid_IsUsable_AndUnchanged(string address)
    {
        var result = _sut.Validate(BlockchainNetwork.TRON, address);

        Assert.Equal(WalletAddressValidationStatus.Valid, result.Status);
        Assert.Equal(address, result.CanonicalAddress);   // base58 is never case-folded
        Assert.Null(result.Reason);
    }

    [Theory]
    // Right shape, broken checksum / wrong version byte → CHECKSUM_MISMATCH.
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u", WalletAddressValidationStatus.ChecksumMismatch)]
    [InlineData("TpSyGx2w2bR9GdrDrYtrYwbvSPhwSmg7ER", WalletAddressValidationStatus.ChecksumMismatch)]
    [InlineData("TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567", WalletAddressValidationStatus.ChecksumMismatch)]
    // Not the shape → INVALID_FORMAT.
    [InlineData("TShort", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("XYz1234567890abcdef1234567890abcde", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj60", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("", WalletAddressValidationStatus.InvalidFormat)]
    public void Tron_Invalid_IsNotUsable(string address, WalletAddressValidationStatus expected)
    {
        var result = _sut.Validate(BlockchainNetwork.TRON, address);

        Assert.Equal(expected, result.Status);
        Assert.False(result.IsUsable);
        Assert.Null(result.CanonicalAddress);
    }

    [Fact]
    public void Tron_Null_IsInvalidFormat()
        => Assert.Equal(WalletAddressValidationStatus.InvalidFormat,
            _sut.Validate(BlockchainNetwork.TRON, null).Status);

    // ---------- Ethereum ----------

    [Theory]
    // EIP-55 spec "all normal" vectors — mixed case, checksum holds.
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [InlineData("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
    [InlineData("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
    [InlineData("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
    public void Ethereum_ChecksummedMixedCase_IsValid_StoredLowercase(string address)
    {
        var result = _sut.Validate(BlockchainNetwork.ETHEREUM, address);

        Assert.Equal(WalletAddressValidationStatus.Valid, result.Status);
        Assert.Equal(address.ToLowerInvariant(), result.CanonicalAddress);
        Assert.Null(result.Reason);
    }

    [Theory]
    // EIP-55 spec "all lower" and "all caps" vectors. Single-case input
    // carries no checksum evidence → usable only after the user's second
    // confirmation (07 §1.3), flagged with CHECKSUM_CONFIRMATION_REQUIRED.
    [InlineData("0xde709f2102306220921060314715629080e2fb77")]
    [InlineData("0x27b1fdb04752bbc536007a920d24acb045561c26")]
    [InlineData("0x52908400098527886E0F7030069857D2E4169EE7")]
    [InlineData("0x8617E340B3D01FA5F11F306F4090FD50E238070D")]
    public void Ethereum_SingleCase_IsValidWithoutChecksum_AndFlagged(string address)
    {
        var result = _sut.Validate(BlockchainNetwork.ETHEREUM, address);

        Assert.Equal(WalletAddressValidationStatus.ValidWithoutChecksum, result.Status);
        Assert.True(result.IsUsable);
        Assert.Equal(address.ToLowerInvariant(), result.CanonicalAddress);
        Assert.Equal(WalletAddressRejectionReasons.ChecksumConfirmationRequired, result.Reason);
    }

    [Theory]
    // Mixed case, one letter flipped (ethers getAddress rejects the first: INVALID_ARGUMENT).
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD", WalletAddressValidationStatus.ChecksumMismatch)]
    [InlineData("0x5AAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", WalletAddressValidationStatus.ChecksumMismatch)]
    // Shape errors.
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAe", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed00", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("0X5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("0xZZAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", WalletAddressValidationStatus.InvalidFormat)]
    [InlineData(" 0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", WalletAddressValidationStatus.InvalidFormat)]
    public void Ethereum_Invalid_IsNotUsable(string address, WalletAddressValidationStatus expected)
    {
        var result = _sut.Validate(BlockchainNetwork.ETHEREUM, address);

        Assert.Equal(expected, result.Status);
        Assert.False(result.IsUsable);
        Assert.NotEqual(WalletAddressRejectionReasons.ChecksumConfirmationRequired, result.Reason);
    }
}
