using System.Text;
using Skinora.Shared.Blockchain;

namespace Skinora.Shared.Tests.Unit.Blockchain;

/// <summary>
/// T145 — Keccak-256 and the EIP-55 checksum (06 §1.4, 07 §9.24). Every
/// expected value below comes from outside this code base:
/// <list type="bullet">
///   <item><description>Keccak-256 digests: ethers v6.17.0 <c>keccak256(toUtf8Bytes(s))</c> (written as ethers prints them, <c>0x</c>-prefixed),
///   run from <c>sidecar-blockchain/node_modules</c> on 2026-10-18. The 135/136/137/300-byte
///   inputs straddle the 136-byte rate so the padding and multi-block paths are covered.</description></item>
///   <item><description>EIP-55 vectors: the test vectors listed in the EIP-55 specification
///   (eips.ethereum.org/EIPS/eip-55), plus the USDT/USDC mainnet contracts as
///   reproduced by ethers v6 <c>getAddress</c>.</description></item>
/// </list>
/// </summary>
public class EvmAddressTests
{
    [Theory]
    [InlineData("", "0xc5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470")]
    [InlineData("abc", "0x4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45")]
    public void Keccak256_KnownDigests(string input, string expectedHex)
    {
        var digest = Keccak256.Hash(Encoding.UTF8.GetBytes(input));
        Assert.Equal(expectedHex, "0x" + Convert.ToHexString(digest).ToLowerInvariant());
    }

    [Theory]
    [InlineData(135, "0x34367dc248bbd832f4e3e69dfaac2f92638bd0bbd18f2912ba4ef454919cf446")]
    [InlineData(136, "0xa6c4d403279fe3e0af03729caada8374b5ca54d8065329a3ebcaeb4b60aa386e")]
    [InlineData(137, "0xd869f639c7046b4929fc92a4d988a8b22c55fbadb802c0c66ebcd484f1915f39")]
    [InlineData(300, "0x5b7e0e47a96f32a88b4f14ca177982790807c40e1a105742ba0fc1babe1ef826")]
    public void Keccak256_BlockBoundaries(int length, string expectedHex)
    {
        var digest = Keccak256.Hash(Encoding.ASCII.GetBytes(new string('a', length)));
        Assert.Equal(expectedHex, "0x" + Convert.ToHexString(digest).ToLowerInvariant());
    }

    [Fact]
    public void Keccak256_IsNotSha3()
    {
        // FIPS-202 SHA3-256("") = a7ffc6f8…; Ethereum's Keccak-256("") = c5d24601…
        // A swap to the framework's SHA3_256 would silently break every checksum.
        var digest = "0x" + Convert.ToHexString(Keccak256.Hash([])).ToLowerInvariant();
        Assert.NotEqual("0xa7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a", digest);
    }

    [Theory]
    // EIP-55 specification vectors.
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [InlineData("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
    [InlineData("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
    [InlineData("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
    // Mainnet USDT / USDC (11 §F8 K1, decision (d)).
    [InlineData("0xdAC17F958D2ee523a2206206994597C13D831ec7")]
    [InlineData("0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48")]
    public void ToChecksum_ReproducesReferenceVectors(string checksummed)
    {
        Assert.Equal(checksummed, EvmAddress.ToChecksum(checksummed.ToLowerInvariant()));
        Assert.Equal(EvmAddressCheck.Checksummed, EvmAddress.Check(checksummed));
    }

    [Theory]
    // EIP-55 "all caps" and "all lower" vectors carry no checksum.
    [InlineData("0x52908400098527886E0F7030069857D2E4169EE7")]
    [InlineData("0x8617E340B3D01FA5F11F306F4090FD50E238070D")]
    [InlineData("0xde709f2102306220921060314715629080e2fb77")]
    [InlineData("0x27b1fdb04752bbc536007a920d24acb045561c26")]
    public void Check_SingleCase_IsNotChecksummed(string address)
    {
        Assert.Equal(EvmAddressCheck.NotChecksummed, EvmAddress.Check(address));
    }

    [Theory]
    // One letter's case flipped in the USDT contract → checksum broken.
    [InlineData("0xdaC17F958D2ee523a2206206994597C13D831ec7")]
    [InlineData("0xdAC17F958D2ee523a2206206994597C13D831Ec7")]
    public void Check_MixedCaseWithBadChecksum_IsInvalid(string address)
    {
        Assert.Equal(EvmAddressCheck.Invalid, EvmAddress.Check(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0xdac17f958d2ee523a2206206994597c13d831ec")]    // 39 hex
    [InlineData("0xdac17f958d2ee523a2206206994597c13d831ec77")]  // 41 hex
    [InlineData("0Xdac17f958d2ee523a2206206994597c13d831ec7")]   // uppercase prefix
    [InlineData("dac17f958d2ee523a2206206994597c13d831ec7xx")]   // no prefix
    [InlineData("0xgac17f958d2ee523a2206206994597c13d831ec7")]   // non-hex
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]           // TRON
    public void Check_Malformed_IsInvalid(string? address)
    {
        Assert.Equal(EvmAddressCheck.Invalid, EvmAddress.Check(address));
    }
}
