using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="SystemSettingsTransferRetryPolicy"/> (T73)
/// — verifies CSV parsing, default fallback, and exhaustion semantics.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SystemSettingsTransferRetryPolicyTests : IDisposable
{
    static SystemSettingsTransferRetryPolicyTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public SystemSettingsTransferRetryPolicyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SeedDefault_Parses_To_1_5_15_Intervals()
    {
        // T73 seed: IsConfigured=true, Value="1,5,15". Regression guard so a
        // future seed edit cannot silently change the documented retry cadence.
        var sut = new SystemSettingsTransferRetryPolicy(_db);
        var max = await sut.GetMaxAttemptsAsync(default);
        Assert.Equal(4, max);  // 3 intervals + 1 first attempt
        Assert.Equal(TimeSpan.FromMinutes(1), await sut.GetRetryDelayAsync(0, default));
        Assert.Equal(TimeSpan.FromMinutes(5), await sut.GetRetryDelayAsync(1, default));
        Assert.Equal(TimeSpan.FromMinutes(15), await sut.GetRetryDelayAsync(2, default));
        Assert.Null(await sut.GetRetryDelayAsync(3, default));
    }

    [Fact]
    public async Task UnconfiguredRow_FallsBackToDefault()
    {
        // Admin nulls out the row via PATCH /admin/settings — provider must
        // still return the documented default rather than throwing.
        var row = await _db.Set<SystemSetting>()
            .FirstAsync(s => s.Key == SystemSettingsTransferRetryPolicy.IntervalsKey);
        row.Value = null;
        row.IsConfigured = false;
        await _db.SaveChangesAsync();

        var sut = new SystemSettingsTransferRetryPolicy(_db);
        Assert.Equal(4, await sut.GetMaxAttemptsAsync(default));
        Assert.Equal(TimeSpan.FromMinutes(1), await sut.GetRetryDelayAsync(0, default));
    }

    [Fact]
    public async Task ConfiguredRow_ParsesCsv()
    {
        await SeedAsync("2,10");
        var sut = new SystemSettingsTransferRetryPolicy(_db);
        Assert.Equal(3, await sut.GetMaxAttemptsAsync(default));
        Assert.Equal(TimeSpan.FromMinutes(2), await sut.GetRetryDelayAsync(0, default));
        Assert.Equal(TimeSpan.FromMinutes(10), await sut.GetRetryDelayAsync(1, default));
        Assert.Null(await sut.GetRetryDelayAsync(2, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("1,0,5")]
    [InlineData("1,-2")]
    public async Task MalformedRow_FallsBackToDefault(string raw)
    {
        await SeedAsync(raw);
        var sut = new SystemSettingsTransferRetryPolicy(_db);
        Assert.Equal(4, await sut.GetMaxAttemptsAsync(default));
        Assert.Equal(TimeSpan.FromMinutes(1), await sut.GetRetryDelayAsync(0, default));
    }

    [Fact]
    public async Task NegativeRetryCount_Throws()
    {
        var sut = new SystemSettingsTransferRetryPolicy(_db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.GetRetryDelayAsync(-1, default));
    }

    private async Task SeedAsync(string value)
    {
        // SystemSettingConfiguration.HasData seeds all 51 catalog rows when
        // EnsureCreated runs; the retry-intervals row already exists, so the
        // test mutates its Value rather than inserting a duplicate (which
        // would trip UQ_SystemSettings_Key).
        var row = await _db.Set<SystemSetting>()
            .FirstAsync(s => s.Key == SystemSettingsTransferRetryPolicy.IntervalsKey);
        row.Value = value;
        row.IsConfigured = true;
        await _db.SaveChangesAsync();
    }
}
