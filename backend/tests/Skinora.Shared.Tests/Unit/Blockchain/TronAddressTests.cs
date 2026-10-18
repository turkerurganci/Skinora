using Skinora.Shared.Blockchain;

namespace Skinora.Shared.Tests.Unit.Blockchain;

/// <summary>
/// T146 — TRON base58check validation (02 §12.3, 07 §1.3). Every expected
/// verdict below comes from outside this code base: tronweb 5.3.5
/// <c>TronWeb.isAddress</c> (and <c>TronWeb.address.fromHex</c> for the
/// generated addresses), run on 2026-10-18 in an isolated scratch install.
/// </summary>
public class TronAddressTests
{
    [Theory]
    // Tether USDT TRC-20 contract (mainnet).
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]
    // fromHex("410000000000000000000000000000000000000000") — the zero account.
    [InlineData("T9yD14Nj9j7xAB4dbGeiX9h8unkKHxuWwb")]
    [InlineData("TLa2f6VPqDgRE67v1736s7bJ8Ray5wYjU7")]
    // fromHex("41" + sha256("skinora-t146-N")[0..40]) for N = 1, 2, 3.
    [InlineData("TWHPPkNRxo1ovxC2uCWmyoir9AiWpt4F2H")]
    [InlineData("THLYcxdu71TwB4HWoQFkMfrhjw5tgH2QkE")]
    [InlineData("TF5hehHaDaMzgAJVZeZg8AyjLKnVxmJ4Hu")]
    public void IsValid_RealAddresses_AreAccepted(string address)
        => Assert.True(TronAddress.IsValid(address));

    [Theory]
    // Last character changed — checksum fails.
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u")]
    // Last character's case flipped — base58 is case-significant.
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6T")]
    // Two adjacent characters swapped.
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLjt6")]
    // Whole address upper-cased.
    [InlineData("TR7NHQJEKQXGTCI8Q8ZY4PL8OTSZGJLJ6T")]
    // A valid address lower-cased ('t' prefix).
    [InlineData("tr7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]
    // Valid base58check over version byte 0x40 / 0x42 instead of 0x41: still
    // 34 characters starting with 'T' — the pre-T146 regex accepted both.
    [InlineData("T1mmJjSMcEVPdma3oiEDah4MBPC3yQsKfi")]
    [InlineData("TpSyGx2w2bR9GdrDrYtrYwbvSPhwSmg7ER")]
    // Too short / too long.
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6")]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6tt")]
    // A character outside the base58 alphabet ('0').
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj60")]
    // Pre-T146 test fixtures: alphabet, length and prefix right, checksum not.
    [InlineData("TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567")]
    [InlineData("TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL")]
    // Base58check of the 20-byte payload under 0x41 with one byte missing
    // (33 characters, not 'T').
    [InlineData("6z7t3TSyVBs8aaTzWUzfkus6r92s6MS25")]
    public void IsValid_Rejects_What_Tronweb_Rejects(string address)
        => Assert.False(TronAddress.IsValid(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t ")]
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    public void IsValid_Rejects_Blank_Padded_And_Evm_Input(string? address)
        => Assert.False(TronAddress.IsValid(address));
}
