using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PaymentAddresses;
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

    /// <summary>
    /// T121 — forces every read to the given non-readable outcome (08 §2.3),
    /// so tests can drive "profile private" and "Steam down" separately from
    /// "inventory read, asset absent". <c>null</c> (default) means the
    /// inventory is readable and the registered items are the truth.
    /// </summary>
    public InventoryVisibility? ForcedVisibility { get; set; }

    /// <summary>
    /// T123 — the freshness every call arrived with, newest last. Lets a test
    /// assert that confirm-ready asked for an uncached read (07 §7.6a) without
    /// reaching into the HTTP layer.
    /// </summary>
    public List<InventoryReadFreshness> ItemReadFreshness { get; } = [];

    /// <summary>T123 — same, for the baseline capture (03 §2.3 step 3).</summary>
    public List<InventoryReadFreshness> BaselineReadFreshness { get; } = [];

    /// <summary>
    /// T123 — forces the baseline capture to a non-readable outcome
    /// independently of <see cref="ForcedVisibility"/>. The two inventories in
    /// play belong to different people: the seller's may be readable while the
    /// buyer's is hidden, which is precisely the case 03 §2.3 says must NOT
    /// block the transaction.
    /// </summary>
    public InventoryVisibility? ForcedBaselineVisibility { get; set; }

    public void Register(string steamId, InventoryItemSnapshot item)
        => _items[(steamId, item.AssetId)] = item;

    public Task<InventoryLookupResult> GetItemAsync(
        string steamId64,
        string itemAssetId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        ItemReadFreshness.Add(freshness);

        if (ForcedVisibility is InventoryVisibility.Private)
            return Task.FromResult(InventoryLookupResult.Private);
        if (ForcedVisibility is InventoryVisibility.Unavailable)
            return Task.FromResult(InventoryLookupResult.Unavailable);

        return Task.FromResult(_items.TryGetValue((steamId64, itemAssetId), out var item)
            ? InventoryLookupResult.Found(item)
            : InventoryLookupResult.NotFound);
    }

    public Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
        string steamId64,
        string classId,
        string? instanceId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        BaselineReadFreshness.Add(freshness);

        if (ForcedBaselineVisibility is InventoryVisibility.Private)
            return Task.FromResult(InventoryClassBaselineResult.Private);
        if (ForcedBaselineVisibility is InventoryVisibility.Unavailable)
            return Task.FromResult(InventoryClassBaselineResult.Unavailable);

        // Counts the registered items of that owner whose class matches — the
        // same (classId, instanceId) rule the sidecar reader applies, so a test
        // registering two copies of one skin gets a baseline of two.
        // T125 — the registered snapshot's asset properties ride along, so tests
        // can exercise the launch-gate capture without a second fixture.
        var assets = _items
            .Where(kv => kv.Key.steamId == steamId64
                && string.Equals(kv.Value.ClassId, classId, StringComparison.Ordinal)
                && (instanceId is null
                    || string.Equals(kv.Value.InstanceId, instanceId, StringComparison.Ordinal)))
            .Select(kv => new InventoryClassAsset(kv.Value.AssetId, kv.Value.AssetProperties))
            .OrderBy(a => a.AssetId, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(InventoryClassBaselineResult.Captured(assets));
    }
}

/// <summary>
/// Test double for <see cref="Skinora.Transactions.Application.Pricing.IMarketPriceProvider"/>.
/// </summary>
internal sealed class FakeMarketPriceProvider : Skinora.Transactions.Application.Pricing.IMarketPriceProvider
{
    public decimal? Price { get; set; }

    public Task<decimal?> TryGetMarketPriceAsync(
        string marketHashName,
        Skinora.Shared.Enums.StablecoinType denomination, CancellationToken cancellationToken)
        => Task.FromResult(Price);
}

/// <summary>
/// Test double for <see cref="IPaymentAddressAllocator"/>. Records every
/// inbound transaction id and, by default, reports
/// <see cref="PaymentAddressAllocationStatus.TransactionIneligible"/> — the
/// status TransactionCreationService treats as a soft-skip. Individual
/// tests can flip <see cref="DefaultStatus"/> or assert on
/// <see cref="Allocations"/> to exercise allocator behavior.
/// </summary>
internal sealed class RecordingPaymentAddressAllocator : IPaymentAddressAllocator
{
    public List<Guid> Allocations { get; } = new();

    public PaymentAddressAllocationStatus DefaultStatus { get; set; }
        = PaymentAddressAllocationStatus.TransactionIneligible;

    public string? DefaultAddress { get; set; }

    public Task<PaymentAddressAllocationResult> AllocateAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        Allocations.Add(transactionId);
        return Task.FromResult(new PaymentAddressAllocationResult(
            DefaultStatus,
            transactionId,
            DefaultAddress,
            HdWalletIndex: null,
            ErrorMessage: null));
    }
}
