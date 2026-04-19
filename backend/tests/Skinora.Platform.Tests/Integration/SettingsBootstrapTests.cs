using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Bootstrap;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SettingsBootstrapService"/> (T26, 06 §8.9):
/// env var hydration for unconfigured rows and startup fail-fast on any
/// remaining <c>IsConfigured = false</c> parameter.
/// </summary>
public class SettingsBootstrapTests : IntegrationTestBase
{
    static SettingsBootstrapTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private SettingsBootstrapService CreateService(
        IDictionary<string, string?>? envOverrides = null,
        AppDbContext? contextOverride = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(envOverrides ?? new Dictionary<string, string?>())
            .Build();

        return new SettingsBootstrapService(
            contextOverride ?? Context,
            configuration,
            NullLogger<SettingsBootstrapService>.Instance);
    }

    private static Dictionary<string, string?> AllRequiredEnvVars() =>
        new()
        {
            ["SKINORA_SETTING_ACCEPT_TIMEOUT_MINUTES"]             = "60",
            ["SKINORA_SETTING_TRADE_OFFER_SELLER_TIMEOUT_MINUTES"] = "60",
            ["SKINORA_SETTING_PAYMENT_TIMEOUT_MIN_MINUTES"]        = "15",
            ["SKINORA_SETTING_PAYMENT_TIMEOUT_MAX_MINUTES"]        = "60",
            ["SKINORA_SETTING_PAYMENT_TIMEOUT_DEFAULT_MINUTES"]    = "30",
            ["SKINORA_SETTING_TRADE_OFFER_BUYER_TIMEOUT_MINUTES"]  = "60",
            ["SKINORA_SETTING_TIMEOUT_WARNING_RATIO"]              = "0.75",
            ["SKINORA_SETTING_MIN_TRANSACTION_AMOUNT"]             = "1.0",
            ["SKINORA_SETTING_MAX_TRANSACTION_AMOUNT"]             = "10000.0",
            ["SKINORA_SETTING_MAX_CONCURRENT_TRANSACTIONS"]        = "5",
            ["SKINORA_SETTING_NEW_ACCOUNT_TRANSACTION_LIMIT"]      = "3",
            ["SKINORA_SETTING_NEW_ACCOUNT_PERIOD_DAYS"]            = "14",
            ["SKINORA_SETTING_CANCEL_LIMIT_COUNT"]                 = "3",
            ["SKINORA_SETTING_CANCEL_LIMIT_PERIOD_HOURS"]          = "24",
            ["SKINORA_SETTING_CANCEL_COOLDOWN_HOURS"]              = "1",
            ["SKINORA_SETTING_PRICE_DEVIATION_THRESHOLD"]          = "0.25",
            ["SKINORA_SETTING_HIGH_VOLUME_AMOUNT_THRESHOLD"]       = "5000.0",
            ["SKINORA_SETTING_HIGH_VOLUME_COUNT_THRESHOLD"]        = "10",
            ["SKINORA_SETTING_HIGH_VOLUME_PERIOD_HOURS"]           = "24",
            ["SKINORA_SETTING_HOT_WALLET_LIMIT"]                   = "100000.0",
        };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_With_All_Required_Env_Vars_Hydrates_And_Completes()
    {
        var service = CreateService(AllRequiredEnvVars());

        await service.ExecuteAsync();

        await using var readCtx = CreateContext();
        var stillMissing = await readCtx.Set<SystemSetting>()
            .Where(s => !s.IsConfigured)
            .Select(s => s.Key)
            .ToListAsync();

        Assert.Empty(stillMissing);

        var accept = await readCtx.Set<SystemSetting>()
            .FirstAsync(s => s.Key == "accept_timeout_minutes");
        Assert.Equal("60", accept.Value);
        Assert.True(accept.IsConfigured);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Throws_When_Required_Parameter_Missing()
    {
        // Seed ships 20 mandatory rows; only hydrate 19 and expect fail-fast.
        var env = AllRequiredEnvVars();
        env.Remove("SKINORA_SETTING_HOT_WALLET_LIMIT");

        var service = CreateService(env);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync());
        Assert.Contains("hot_wallet_limit", ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Throws_When_Env_Value_Fails_DataType_Validation()
    {
        var env = AllRequiredEnvVars();
        env["SKINORA_SETTING_ACCEPT_TIMEOUT_MINUTES"] = "not-an-int";

        var service = CreateService(env);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync());
        Assert.Contains("accept_timeout_minutes", ex.Message);
        Assert.Contains("not an integer", ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Does_Not_Override_Already_Configured_Parameters()
    {
        // Pre-existing configured value must survive an env override attempt —
        // 06 §8.9 security clause.
        var env = AllRequiredEnvVars();
        env["SKINORA_SETTING_COMMISSION_RATE"] = "0.99"; // malicious override

        var service = CreateService(env);
        await service.ExecuteAsync();

        await using var readCtx = CreateContext();
        var commission = await readCtx.Set<SystemSetting>()
            .FirstAsync(s => s.Key == "commission_rate");
        Assert.Equal("0.02", commission.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Is_Idempotent_When_All_Configured()
    {
        var service = CreateService(AllRequiredEnvVars());
        await service.ExecuteAsync();

        // Second run with no env vars must still succeed because every row is
        // now IsConfigured = true.
        var secondRun = CreateService();
        await secondRun.ExecuteAsync();

        await using var readCtx = CreateContext();
        var unconfigured = await readCtx.Set<SystemSetting>()
            .CountAsync(s => !s.IsConfigured);
        Assert.Equal(0, unconfigured);
    }
}
