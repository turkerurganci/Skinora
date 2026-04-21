using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class SettingsBasedGeoBlockCheckTests : IntegrationTestBase
{
    static SettingsBasedGeoBlockCheckTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private sealed class StubCountryResolver : ICountryResolver
    {
        public string? Country { get; set; }
        public string? ResolveCountry(HttpContext? httpContext, string? ipAddress) => Country;
    }

    private async Task SetBannedAsync(string value)
    {
        // The T30 migration seeds auth.banned_countries = "NONE" on DB
        // creation. Tests update the existing row rather than insert.
        var existing = await Context.Set<SystemSetting>()
            .SingleOrDefaultAsync(s => s.Key == SettingsBasedGeoBlockCheck.SettingKey);

        if (existing is null)
        {
            Context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = SettingsBasedGeoBlockCheck.SettingKey,
                Value = value,
                IsConfigured = true,
                DataType = "string",
                Category = "AccessControl",
                Description = "test",
            });
        }
        else
        {
            existing.Value = value;
            existing.IsConfigured = true;
        }

        await Context.SaveChangesAsync();
    }

    private SettingsBasedGeoBlockCheck CreateSut(string? resolvedCountry)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var resolver = new StubCountryResolver { Country = resolvedCountry };
        return new SettingsBasedGeoBlockCheck(
            Context, accessor, resolver,
            NullLogger<SettingsBasedGeoBlockCheck>.Instance);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_CountryNull_ReturnsAllowed()
    {
        await SetBannedAsync("IR,KP,CU");

        var decision = await CreateSut(null).EvaluateAsync("1.2.3.4", default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_BannedCountryMatched_ReturnsBlocked()
    {
        await SetBannedAsync("IR,KP,CU");

        var decision = await CreateSut("IR").EvaluateAsync(null, default);

        Assert.True(decision.IsBlocked);
        Assert.Equal("IR", decision.CountryCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_AllowedCountry_ReturnsAllowed()
    {
        await SetBannedAsync("IR,KP,CU");

        var decision = await CreateSut("TR").EvaluateAsync(null, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_NoneMarker_AllowsEverything()
    {
        await SetBannedAsync("NONE");

        var decision = await CreateSut("IR").EvaluateAsync(null, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_EmptyList_AllowsEverything()
    {
        await SetBannedAsync("");

        var decision = await CreateSut("IR").EvaluateAsync(null, default);

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EvaluateAsync_CaseInsensitive_ReturnsBlocked()
    {
        await SetBannedAsync("ir, kp");

        var decision = await CreateSut("IR").EvaluateAsync(null, default);

        Assert.True(decision.IsBlocked);
    }
}
