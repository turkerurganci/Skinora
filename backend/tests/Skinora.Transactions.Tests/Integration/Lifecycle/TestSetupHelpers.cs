using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// Per-test helpers shared across the lifecycle integration suite. Lives
/// outside <see cref="Skinora.Shared.Tests.Integration.IntegrationTestBase"/>
/// so the base stays generic.
/// </summary>
internal static class TestSetupHelpers
{
    /// <summary>
    /// Sets the SystemSetting row for <paramref name="key"/> to a configured
    /// value, inserting if missing. Used to flip individual T45 settings on
    /// per test without depending on the migration seed (some seed rows ship
    /// as <c>IsConfigured=false</c>). Key is the natural unique key, so we
    /// look up by Key rather than by Id to coexist with the seeded rows.
    /// </summary>
    public static async Task ConfigureSettingAsync(this AppDbContext context, string key, string value)
    {
        var existing = await context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                IsConfigured = true,
                DataType = "string",
                Category = "Test",
            });
        }
        else
        {
            existing.Value = value;
            existing.IsConfigured = true;
        }
        await context.SaveChangesAsync();
    }
}

/// <summary>
/// Test double for <see cref="ISteamInventoryReader"/>. Returns the
/// configured snapshot for a single (steamId, assetId) tuple, otherwise
/// <c>null</c> — mirrors the production stub's fail-closed behavior.
/// </summary>
internal sealed class FakeSteamInventoryReader : ISteamInventoryReader
{
    private readonly Dictionary<(string steamId, string assetId), InventoryItemSnapshot> _items = [];

    public void Register(string steamId, InventoryItemSnapshot item)
        => _items[(steamId, item.AssetId)] = item;

    public Task<InventoryItemSnapshot?> TryGetItemAsync(
        string steamId64, string itemAssetId, CancellationToken cancellationToken)
        => Task.FromResult(_items.TryGetValue((steamId64, itemAssetId), out var item) ? item : null);
}

/// <summary>
/// Test double for <see cref="Skinora.Transactions.Application.Pricing.IMarketPriceProvider"/>.
/// </summary>
internal sealed class FakeMarketPriceProvider : Skinora.Transactions.Application.Pricing.IMarketPriceProvider
{
    public decimal? Price { get; set; }

    public Task<decimal?> TryGetMarketPriceAsync(
        string itemClassId, string? itemInstanceId,
        Skinora.Shared.Enums.StablecoinType denomination, CancellationToken cancellationToken)
        => Task.FromResult(Price);
}
