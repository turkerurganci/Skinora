using Skinora.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Platform.Infrastructure.Configuration;

namespace Skinora.Platform.Tests.Unit.Wallets;

/// <summary>
/// The platform's own wallet addresses are deployment configuration (05 §3.3,
/// owner decision 2026-09-16). This fixes what "unset" and "malformed" mean:
/// unset is a legal state every caller degrades on, malformed stops the host
/// before it can queue a sweep to a typo.
/// </summary>
[Trait("Category", "Unit")]
public class EnvPlatformWalletAddressProviderTests
{
    private const string ValidHot = "TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE";
    private const string ValidCold = "TGpQ6KteKAbJDu7zZuoRnUvUTxvjKG4tv5";

    private static EnvPlatformWalletAddressProvider Build(params (string Key, string? Value)[] entries)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e =>
                new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

        return new EnvPlatformWalletAddressProvider(
            configuration, NullLogger<EnvPlatformWalletAddressProvider>.Instance);
    }

    [Fact]
    public void Reads_Both_Addresses_From_Configuration()
    {
        var provider = Build(
            (EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, ValidHot),
            (EnvPlatformWalletAddressProvider.ColdWalletConfigurationKey, ValidCold));

        Assert.Equal(ValidHot, provider.HotWalletAddress);
        Assert.Equal(ValidCold, provider.ColdWalletAddress);
    }

    [Fact]
    public void Trims_Surrounding_Whitespace()
    {
        var provider = Build(
            (EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, $"  {ValidHot}\t"));

        Assert.Equal(ValidHot, provider.HotWalletAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NONE")]
    public void Unset_And_The_Inherited_NONE_Sentinel_Both_Mean_Unconfigured(string? raw)
    {
        var provider = Build(
            (EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, raw),
            (EnvPlatformWalletAddressProvider.ColdWalletConfigurationKey, raw));

        Assert.Null(provider.HotWalletAddress);
        Assert.Null(provider.ColdWalletAddress);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("0x742d35Cc6634C0532925a3b844Bc454e4438f44e")]   // Ethereum-shaped
    [InlineData("TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44j")]             // one char short
    [InlineData("TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE0")]           // base58 excludes 0
    [InlineData("none")]                                          // sentinel is case-sensitive
    public void Malformed_Address_Fails_The_Host_Rather_Than_Degrading(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build((EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, raw)));

        Assert.Contains(EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, ex.Message);
    }

    // -------------------- T148 — one pair per network (05 §3.3a) --------------------

    private const string EthHotChecksummed = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";
    private const string EthColdLower = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";

    [Fact]
    public void Each_Network_Reads_Its_Own_Variables()
    {
        var provider = Build(
            (EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, ValidHot),
            (EnvPlatformWalletAddressProvider.ColdWalletConfigurationKey, ValidCold),
            (EnvPlatformWalletAddressProvider.EthereumHotWalletConfigurationKey, EthHotChecksummed),
            (EnvPlatformWalletAddressProvider.EthereumColdWalletConfigurationKey, EthColdLower));

        var tron = provider.For(BlockchainNetwork.TRON);
        Assert.Equal((ValidHot, ValidCold), (tron.HotWalletAddress, tron.ColdWalletAddress));
        // The legacy properties stay TRON's.
        Assert.Equal(ValidHot, provider.HotWalletAddress);

        var eth = provider.For(BlockchainNetwork.ETHEREUM);
        // Stored in the canonical lowercase form (06 §1.4).
        Assert.Equal(EthHotChecksummed.ToLowerInvariant(), eth.HotWalletAddress);
        Assert.Equal(EthColdLower, eth.ColdWalletAddress);
    }

    [Fact]
    public void Ethereum_Unset_Leaves_Tron_Configured_And_Vice_Versa()
    {
        var tronOnly = Build((EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, ValidHot));
        Assert.Null(tronOnly.For(BlockchainNetwork.ETHEREUM).HotWalletAddress);
        Assert.Null(tronOnly.For(BlockchainNetwork.ETHEREUM).ColdWalletAddress);

        var ethOnly = Build((EnvPlatformWalletAddressProvider.EthereumHotWalletConfigurationKey, EthHotChecksummed));
        Assert.Null(ethOnly.For(BlockchainNetwork.TRON).HotWalletAddress);
        Assert.NotNull(ethOnly.For(BlockchainNetwork.ETHEREUM).HotWalletAddress);
    }

    [Theory]
    [InlineData("TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE")]            // a TRON address
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD")]    // EIP-55 checksum broken
    [InlineData("0x5aaeb6053f3e94c9b9a09f33669435e7ef1bea")]      // one byte short
    [InlineData("5aaeb6053f3e94c9b9a09f33669435e7ef1beaed")]      // no 0x
    public void Malformed_Ethereum_Address_Fails_The_Host(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build((EnvPlatformWalletAddressProvider.EthereumColdWalletConfigurationKey, raw)));

        Assert.Contains(EnvPlatformWalletAddressProvider.EthereumColdWalletConfigurationKey, ex.Message);
    }

    [Fact]
    public void One_Address_Configured_Does_Not_Imply_The_Other()
    {
        var provider = Build(
            (EnvPlatformWalletAddressProvider.HotWalletConfigurationKey, ValidHot));

        Assert.Equal(ValidHot, provider.HotWalletAddress);
        Assert.Null(provider.ColdWalletAddress);
    }
}
