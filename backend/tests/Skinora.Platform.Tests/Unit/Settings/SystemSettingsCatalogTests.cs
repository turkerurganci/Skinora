using Skinora.Platform.Application.Settings;
using Skinora.Platform.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Unit.Settings;

/// <summary>
/// Catalog ↔ seed coverage check (T41). Every seeded SystemSetting key must
/// have a <see cref="SystemSettingsCatalog"/> entry, and the catalog must not
/// reference unknown keys. Without this guard a future migration that adds a
/// row would produce an invisible setting (07 §9.8 omits keys missing from the
/// catalog by design — see <see cref="SystemSettingsService.ListAsync"/>).
/// </summary>
public class SystemSettingsCatalogTests
{
    [Fact]
    public void Catalog_Covers_Every_Seeded_Key()
    {
        var seedKeys = SystemSettingSeed.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        // Env-sourced entries are catalog-only by design: they have no
        // SystemSetting row because their value is deployment configuration
        // (05 §3.3, owner decision 2026-09-16). They are excluded here rather
        // than from the catalog so the panel still lists them, read-only.
        var catalogKeys = SystemSettingsCatalog.All
            .Where(m => !m.EnvSourced)
            .Select(m => m.Key)
            .ToHashSet(StringComparer.Ordinal);

        var missingFromCatalog = seedKeys.Except(catalogKeys).OrderBy(k => k).ToList();
        var orphanedInCatalog = catalogKeys.Except(seedKeys).OrderBy(k => k).ToList();

        Assert.Empty(missingFromCatalog);
        Assert.Empty(orphanedInCatalog);
    }

    /// <summary>
    /// The exclusion above must stay narrow: exactly the platform wallet
    /// addresses — one hot/cold pair per network since T148 (05 §3.3a) — and
    /// each one must really be seedless. Another env-sourced entry, or a seed
    /// row sneaking back under one of these keys, would make the panel show a
    /// value nothing keeps in sync.
    /// </summary>
    [Fact]
    public void Env_Sourced_Entries_Are_The_Per_Network_Wallet_Addresses_And_Have_No_Seed_Row()
    {
        var envSourced = SystemSettingsCatalog.All
            .Where(m => m.EnvSourced)
            .Select(m => m.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var walletKeys = new[]
        {
            SystemSettingsCatalog.EthereumColdWalletAddressKey,
            SystemSettingsCatalog.EthereumHotWalletAddressKey,
            SystemSettingsCatalog.ColdWalletAddressKey,
            SystemSettingsCatalog.HotWalletAddressKey,
        };
        Assert.Equal(walletKeys, envSourced);

        var seedKeys = SystemSettingSeed.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in walletKeys)
            Assert.DoesNotContain(key, seedKeys);
    }

    /// <summary>
    /// T148 — every per-network key the code reads
    /// (<see cref="Skinora.Shared.Blockchain.BlockchainNetworkSettingKeys"/>)
    /// is a seeded row with an admin catalogue entry, so no network's
    /// parameter is invisible or unconfigurable.
    /// </summary>
    [Theory]
    [InlineData(Skinora.Shared.Enums.BlockchainNetwork.TRON)]
    [InlineData(Skinora.Shared.Enums.BlockchainNetwork.ETHEREUM)]
    public void Every_Per_Network_Key_Is_Seeded_And_Catalogued(Skinora.Shared.Enums.BlockchainNetwork network)
    {
        var seedKeys = SystemSettingSeed.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var key in Skinora.Shared.Blockchain.BlockchainNetworkSettingKeys.For(network).AllKeys())
        {
            Assert.Contains(key, seedKeys);
            Assert.True(SystemSettingsCatalog.Contains(key), $"{key} has no catalogue entry");
        }
    }

    /// <summary>
    /// T148 — Ethereum's keys never alias TRON's: a shared key would apply one
    /// network's number to the other.
    /// </summary>
    [Fact]
    public void Ethereum_Keys_Do_Not_Reuse_Tron_Keys()
    {
        var tron = Skinora.Shared.Blockchain.BlockchainNetworkSettingKeys.Tron.AllKeys();
        var ethereum = Skinora.Shared.Blockchain.BlockchainNetworkSettingKeys.Ethereum.AllKeys();

        Assert.Empty(tron.Intersect(ethereum, StringComparer.Ordinal));
        Assert.All(ethereum, k => Assert.StartsWith("blockchain.ethereum.", k, StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_Has_No_Duplicate_Keys()
    {
        var keys = SystemSettingsCatalog.All.Select(m => m.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("int", "number")]
    [InlineData("decimal", "number")]
    [InlineData("bool", "boolean")]
    [InlineData("string", "string")]
    public void ValueTypeFor_Maps_DataType_To_Api_ValueType(string dataType, string expected)
    {
        Assert.Equal(expected, SystemSettingsCatalog.ValueTypeFor(dataType));
    }

    [Fact]
    public void Every_Catalog_Entry_Has_NonEmpty_ApiCategory_And_Label()
    {
        foreach (var meta in SystemSettingsCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(meta.ApiCategory), $"category empty for {meta.Key}");
            Assert.False(string.IsNullOrWhiteSpace(meta.Label), $"label empty for {meta.Key}");
        }
    }
}
