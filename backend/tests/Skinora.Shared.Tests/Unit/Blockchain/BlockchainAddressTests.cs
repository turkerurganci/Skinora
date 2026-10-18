using Skinora.Shared.Blockchain;
using Skinora.Shared.Persistence.Converters;

namespace Skinora.Shared.Tests.Unit.Blockchain;

/// <summary>
/// T145 — the single canonical form of an on-chain identifier (06 §1.4).
/// The two halves of the rule are pinned separately: EVM hex folds to
/// lowercase and compares case-insensitively; TRON base58 is never touched
/// and compares case-sensitively (base58 is case-significant — the two
/// spellings below are two different addresses).
/// </summary>
public class BlockchainAddressTests
{
    // Tether USDT on Ethereum, EIP-55 checksummed (independently reproduced by
    // ethers v6 getAddress) and its lowercase canonical form.
    private const string EvmChecksummed = "0xdAC17F958D2ee523a2206206994597C13D831ec7";
    private const string EvmLower = "0xdac17f958d2ee523a2206206994597c13d831ec7";
    private const string EvmUpper = "0xDAC17F958D2EE523A2206206994597C13D831EC7";

    // Tether USDT on TRON (base58check) and the same characters in lowercase —
    // a different string the chain treats as a different (invalid) address.
    private const string TronAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    private const string TronLowercased = "tr7nhqjekqxgtci8q8zy4pl8otszgjlj6t";

    // A TRON transaction id is 64 hex chars without a prefix (synthetic,
    // built at run time so the fixture is not mistaken for key material).
    private static readonly string TronTxId = string.Concat(Enumerable.Repeat("4f1e0bD2", 8));

    [Theory]
    [InlineData(EvmChecksummed)]
    [InlineData(EvmUpper)]
    [InlineData(EvmLower)]
    public void Canonicalize_EvmAddress_IsLowercase(string spelling)
    {
        Assert.Equal(EvmLower, BlockchainAddress.Canonicalize(spelling));
    }

    [Fact]
    public void Canonicalize_EvmTxHash_IsLowercase()
    {
        var hash = "0xAbCdEf0123456789aBcDeF0123456789AbCdEf0123456789aBcDeF0123456789";
        Assert.Equal(hash.ToLowerInvariant(), BlockchainAddress.Canonicalize(hash));
    }

    [Theory]
    [InlineData(TronAddress)]
    [InlineData(TronLowercased)]
    [InlineData("TXyz1234567890ABCDEFabcdef1234567")]
    public void Canonicalize_NonEvmValue_IsUnchanged(string value)
    {
        // The rule never lowercases a TRON value: a base58 address cannot
        // start with "0x" (base58 has no '0' digit).
        Assert.Same(value, BlockchainAddress.Canonicalize(value));
    }

    [Fact]
    public void Canonicalize_TronTxId_IsUnchanged()
    {
        // Mixed-case on purpose: without the 0x prefix the value is not
        // treated as EVM hex, so even its letter case is left alone.
        Assert.Same(TronTxId, BlockchainAddress.Canonicalize(TronTxId));
    }

    [Theory]
    [InlineData("0x")]            // prefix only
    [InlineData("0xZZ12")]        // non-hex after the prefix
    [InlineData(" 0xABCDEF")]     // leading space — not trimmed, not hex-shaped
    public void Canonicalize_NotZeroXHex_IsUnchanged(string value)
    {
        Assert.Equal(value, BlockchainAddress.Canonicalize(value));
    }

    [Fact]
    public void AreEqual_EvmSpellings_AreEqual()
    {
        Assert.True(BlockchainAddress.AreEqual(EvmChecksummed, EvmLower));
        Assert.True(BlockchainAddress.AreEqual(EvmUpper, EvmChecksummed));
    }

    [Fact]
    public void AreEqual_TronDifferentCase_IsNotEqual()
    {
        Assert.False(BlockchainAddress.AreEqual(TronAddress, TronLowercased));
        Assert.True(BlockchainAddress.AreEqual(TronAddress, TronAddress));
    }

    [Fact]
    public void AreEqual_Null_NeverMatches()
    {
        Assert.False(BlockchainAddress.AreEqual(null, null));
        Assert.False(BlockchainAddress.AreEqual(EvmLower, null));
        Assert.False(BlockchainAddress.AreEqual(null, TronAddress));
    }

    [Fact]
    public void Comparer_KeysSetsByCanonicalForm()
    {
        var set = new HashSet<string>(BlockchainAddress.Comparer) { EvmChecksummed, TronAddress };

        Assert.Contains(EvmLower, set);
        Assert.Contains(EvmUpper, set);
        Assert.Contains(TronAddress, set);
        Assert.DoesNotContain(TronLowercased, set);
    }

    [Fact]
    public void Converter_StoresCanonicalForm_BothDirections()
    {
        var converter = CanonicalChainIdentifierConverter.Instance;
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        Assert.Equal(EvmLower, toProvider(EvmChecksummed));
        Assert.Equal(EvmLower, fromProvider(EvmUpper));
        Assert.Equal(TronAddress, toProvider(TronAddress));
        Assert.Equal(TronAddress, fromProvider(TronAddress));
    }
}
