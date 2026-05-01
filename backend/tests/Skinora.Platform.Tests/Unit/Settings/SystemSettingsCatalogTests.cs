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
        var catalogKeys = SystemSettingsCatalog.All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

        var missingFromCatalog = seedKeys.Except(catalogKeys).OrderBy(k => k).ToList();
        var orphanedInCatalog = catalogKeys.Except(seedKeys).OrderBy(k => k).ToList();

        Assert.Empty(missingFromCatalog);
        Assert.Empty(orphanedInCatalog);
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
