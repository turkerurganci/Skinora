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
    /// The exclusion above must stay narrow: exactly the two platform wallet
    /// addresses, and each one must really be seedless. A third env-sourced
    /// entry, or a seed row sneaking back under one of these keys, would make
    /// the panel show a value nothing keeps in sync.
    /// </summary>
    [Fact]
    public void Env_Sourced_Entries_Are_The_Two_Wallet_Addresses_And_Have_No_Seed_Row()
    {
        var envSourced = SystemSettingsCatalog.All
            .Where(m => m.EnvSourced)
            .Select(m => m.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[]
            {
                SystemSettingsCatalog.ColdWalletAddressKey,
                SystemSettingsCatalog.HotWalletAddressKey,
            },
            envSourced);

        var seedKeys = SystemSettingSeed.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(SystemSettingsCatalog.HotWalletAddressKey, seedKeys);
        Assert.DoesNotContain(SystemSettingsCatalog.ColdWalletAddressKey, seedKeys);
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
