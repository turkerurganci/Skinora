using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Domain.Entities;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class RefreshTokenCleanupJobTests : IntegrationTestBase
{
    static RefreshTokenCleanupJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
    }

    private RefreshTokenCleanupJob CreateSut() =>
        new(Context, NullLogger<RefreshTokenCleanupJob>.Instance);

    private async Task<Guid> SeedUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000099",
            SteamDisplayName = "CleanupTester",
            PreferredLanguage = "en",
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<RefreshToken> AddTokenAsync(
        Guid userId, DateTime expiresAt, bool isRevoked = false, DateTime? revokedAt = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAt = expiresAt,
            IsRevoked = isRevoked,
            RevokedAt = revokedAt,
        };
        Context.Set<RefreshToken>().Add(token);
        await Context.SaveChangesAsync();
        return token;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_NoStaleTokens_ReturnsZero()
    {
        var userId = await SeedUserAsync();
        await AddTokenAsync(userId, DateTime.UtcNow.AddDays(3));

        var count = await CreateSut().ExecuteAsync(default);

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ExpiredPastGrace_SoftDeletes()
    {
        var userId = await SeedUserAsync();
        var expired = await AddTokenAsync(
            userId, DateTime.UtcNow - RefreshTokenCleanupJob.GracePeriod - TimeSpan.FromHours(1));
        var fresh = await AddTokenAsync(userId, DateTime.UtcNow.AddDays(3));

        var count = await CreateSut().ExecuteAsync(default);

        Assert.Equal(1, count);

        await using var verify = CreateContext();
        var visibleIds = await verify.Set<RefreshToken>()
            .AsNoTracking()
            .Select(t => t.Id)
            .ToListAsync();
        Assert.DoesNotContain(expired.Id, visibleIds);
        Assert.Contains(fresh.Id, visibleIds);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RecentlyExpired_WithinGrace_KeepsVisible()
    {
        var userId = await SeedUserAsync();
        // Expired but still inside the 7-day grace window.
        var recent = await AddTokenAsync(
            userId, DateTime.UtcNow - TimeSpan.FromHours(2));

        var count = await CreateSut().ExecuteAsync(default);

        Assert.Equal(0, count);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<RefreshToken>().AnyAsync(t => t.Id == recent.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RevokedPastGrace_SoftDeletes()
    {
        var userId = await SeedUserAsync();
        var revokedAt = DateTime.UtcNow - RefreshTokenCleanupJob.GracePeriod - TimeSpan.FromHours(1);
        var revoked = await AddTokenAsync(
            userId, DateTime.UtcNow.AddDays(3), isRevoked: true, revokedAt: revokedAt);

        var count = await CreateSut().ExecuteAsync(default);

        Assert.Equal(1, count);

        await using var verify = CreateContext();
        Assert.False(await verify.Set<RefreshToken>().AnyAsync(t => t.Id == revoked.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RevokedWithinGrace_KeepsVisible()
    {
        var userId = await SeedUserAsync();
        var recent = await AddTokenAsync(
            userId, DateTime.UtcNow.AddDays(3),
            isRevoked: true, revokedAt: DateTime.UtcNow.AddDays(-1));

        var count = await CreateSut().ExecuteAsync(default);

        Assert.Equal(0, count);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<RefreshToken>().AnyAsync(t => t.Id == recent.Id));
    }
}
