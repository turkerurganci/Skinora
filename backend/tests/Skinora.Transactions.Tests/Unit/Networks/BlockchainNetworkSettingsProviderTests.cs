using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Networks;

/// <summary>
/// Unit coverage for the three <see cref="BlockchainNetworkSettingsProvider"/>
/// fields T149 added (11 §F8 K8, 02 §4.6, 06 §3.17 seed 76–78): the TRON
/// network fee buffer and fallback, and Ethereum's own minimum-refund
/// multiplier. Pins that an admin-set value is read as set — the buffer's 0
/// ("no head-room") included, which the positive-only reads of the other
/// fields would turn into the default — and that out-of-envelope values fall
/// back to the seeded default (T149 validation, finding M23).
/// </summary>
[Trait("Category", "Unit")]
public sealed class BlockchainNetworkSettingsProviderTests : IDisposable
{
    static BlockchainNetworkSettingsProviderTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly BlockchainNetworkSettingsProvider _sut;

    public BlockchainNetworkSettingsProviderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new BlockchainNetworkSettingsProvider(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SeededRows_AreRead_TronHasTheFeeLineSettings_EthereumHasItsOwnThreshold()
    {
        var tron = await _sut.GetAsync(BlockchainNetwork.TRON, default);
        var ethereum = await _sut.GetAsync(BlockchainNetwork.ETHEREUM, default);

        Assert.Equal(0.007m, tron.NetworkFeeBufferRatio);
        Assert.Equal(3.00m, tron.NetworkFeeFallbackUsdt);
        Assert.Equal(2.0m, tron.MinRefundThresholdRatio);

        // Ethereum has no network fee line (K8): both fields are absent, not 0.
        Assert.Null(ethereum.NetworkFeeBufferRatio);
        Assert.Null(ethereum.NetworkFeeFallbackUsdt);
        Assert.Equal(2.0m, ethereum.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task AdminSetValues_AreReadAsSet_AndEthereumsThresholdDoesNotMoveTrons()
    {
        await SetAsync(BlockchainNetworkSettingKeys.Tron.NetworkFeeBufferRatio!, "0.05");
        await SetAsync(BlockchainNetworkSettingKeys.Tron.NetworkFeeFallbackUsdt!, "4.25");
        await SetAsync(BlockchainNetworkSettingKeys.Ethereum.MinRefundThresholdRatio, "3.5");

        var tron = await _sut.GetAsync(BlockchainNetwork.TRON, default);
        var ethereum = await _sut.GetAsync(BlockchainNetwork.ETHEREUM, default);

        Assert.Equal(0.05m, tron.NetworkFeeBufferRatio);
        Assert.Equal(4.25m, tron.NetworkFeeFallbackUsdt);
        Assert.Equal(3.5m, ethereum.MinRefundThresholdRatio);
        Assert.Equal(2.0m, tron.MinRefundThresholdRatio);
    }

    [Fact]
    public async Task BufferZero_IsReadAsZero_NotReplacedByTheDefault()
    {
        // 0 is a valid buffer ("no head-room", 06 §3.17: 0 ≤ value < 1). A read
        // that accepted only positive values would silently put 0.007 back.
        await SetAsync(BlockchainNetworkSettingKeys.Tron.NetworkFeeBufferRatio!, "0");

        var tron = await _sut.GetAsync(BlockchainNetwork.TRON, default);

        Assert.Equal(0m, tron.NetworkFeeBufferRatio);
    }

    [Theory]
    [InlineData("1")]          // upper bound is exclusive
    [InlineData("1.5")]
    [InlineData("-0.001")]
    [InlineData("abc")]
    public async Task BufferOutsideTheEnvelope_FallsBackToTheSeededDefault(string raw)
    {
        await SetAsync(BlockchainNetworkSettingKeys.Tron.NetworkFeeBufferRatio!, raw);

        var tron = await _sut.GetAsync(BlockchainNetwork.TRON, default);

        Assert.Equal(0.007m, tron.NetworkFeeBufferRatio);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task NonPositiveOrGarbageFallbackAndThreshold_FallBackToTheSeededDefaults(string raw)
    {
        await SetAsync(BlockchainNetworkSettingKeys.Tron.NetworkFeeFallbackUsdt!, raw);
        await SetAsync(BlockchainNetworkSettingKeys.Ethereum.MinRefundThresholdRatio, raw);

        var tron = await _sut.GetAsync(BlockchainNetwork.TRON, default);
        var ethereum = await _sut.GetAsync(BlockchainNetwork.ETHEREUM, default);

        Assert.Equal(3.00m, tron.NetworkFeeFallbackUsdt);
        Assert.Equal(2.0m, ethereum.MinRefundThresholdRatio);
    }

    private async Task SetAsync(string key, string value)
    {
        var row = await _db.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        row.Value = value;
        row.IsConfigured = true;
        await _db.SaveChangesAsync();
    }
}
