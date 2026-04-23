using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Auth.Domain.Entities;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class RefreshTokenServiceTests : IntegrationTestBase
{
    static RefreshTokenServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
    }

    private readonly JwtSettings _settings = new()
    {
        Secret = "refresh-service-test-secret-key-minimum-32!!",
        Issuer = "skinora-test",
        Audience = "skinora-test",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7,
    };

    private RefreshTokenService CreateSut(IRefreshTokenCache? cache = null)
    {
        var options = Options.Create(_settings);
        var generator = new RefreshTokenGenerator(Context, options);
        var access = new AccessTokenGenerator(options);
        return new RefreshTokenService(
            Context, cache ?? new NullRefreshTokenCache(), access, generator, options);
    }

    private async Task<User> CreateUserAsync(string steamId = "76561198000000001")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = steamId,
            SteamDisplayName = "Tester",
            PreferredLanguage = "en",
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    private async Task<GeneratedRefreshToken> IssueTokenAsync(Guid userId)
    {
        var generator = new RefreshTokenGenerator(Context, Options.Create(_settings));
        return await generator.IssueAsync(userId, "1.2.3.4", "agent", CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_MissingPlaintext_ReturnsMissing()
    {
        var sut = CreateSut();

        var outcome = await sut.RotateAsync("", null, null, default);

        Assert.IsType<RotateOutcome.Missing>(outcome);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_UnknownToken_ReturnsInvalid()
    {
        var sut = CreateSut();

        var outcome = await sut.RotateAsync("unknown-plaintext", null, null, default);

        Assert.IsType<RotateOutcome.Invalid>(outcome);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_HappyPath_RevokesOldAndIssuesNewPair()
    {
        var user = await CreateUserAsync();
        var original = await IssueTokenAsync(user.Id);
        var sut = CreateSut();

        var outcome = await sut.RotateAsync(original.PlainTextToken, "1.2.3.4", "agent", default);

        var success = Assert.IsType<RotateOutcome.Success>(outcome);
        Assert.Equal(user.Id, success.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(success.Access.Token));
        Assert.NotEqual(original.PlainTextToken, success.Refresh.PlainTextToken);

        // Reload from a fresh context so we see the persisted state, not the
        // tracked instance.
        await using var verify = CreateContext();
        var reloaded = await verify.Set<RefreshToken>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == original.Entity.Id);
        Assert.True(reloaded.IsRevoked);
        Assert.NotNull(reloaded.RevokedAt);
        Assert.Equal(success.Refresh.Entity.Id, reloaded.ReplacedByTokenId);

        var count = await verify.Set<RefreshToken>().CountAsync(t => t.UserId == user.Id);
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_AlreadyRevokedToken_MassRevokesAndReturnsReused()
    {
        var user = await CreateUserAsync();
        var compromised = await IssueTokenAsync(user.Id);
        var siblingActive = await IssueTokenAsync(user.Id);

        // Pre-revoke the compromised token to simulate the attacker presenting
        // an already-rotated copy.
        compromised.Entity.IsRevoked = true;
        compromised.Entity.RevokedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var outcome = await sut.RotateAsync(compromised.PlainTextToken, null, null, default);

        var reused = Assert.IsType<RotateOutcome.Reused>(outcome);
        Assert.Equal(user.Id, reused.UserId);

        await using var verify = CreateContext();
        var sibling = await verify.Set<RefreshToken>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == siblingActive.Entity.Id);
        Assert.True(sibling.IsRevoked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_AlreadyRotatedToken_MassRevokesAndReturnsReused()
    {
        var user = await CreateUserAsync();
        var original = await IssueTokenAsync(user.Id);

        // First rotation — legitimate, produces a successor.
        var sut = CreateSut();
        var first = await sut.RotateAsync(original.PlainTextToken, null, null, default);
        Assert.IsType<RotateOutcome.Success>(first);

        // Second rotation with the original (now rotated) token — reuse signal.
        var second = await sut.RotateAsync(original.PlainTextToken, null, null, default);

        Assert.IsType<RotateOutcome.Reused>(second);

        // The successor issued during the first rotation must also be killed.
        var successor = ((RotateOutcome.Success)first).Refresh.Entity.Id;
        await using var verify = CreateContext();
        var successorRow = await verify.Set<RefreshToken>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == successor);
        Assert.True(successorRow.IsRevoked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RotateAsync_ExpiredToken_ReturnsExpired()
    {
        var user = await CreateUserAsync();
        var token = await IssueTokenAsync(user.Id);

        token.Entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await Context.SaveChangesAsync();

        var sut = CreateSut();
        var outcome = await sut.RotateAsync(token.PlainTextToken, null, null, default);

        Assert.IsType<RotateOutcome.Expired>(outcome);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeAsync_UnknownToken_ReturnsFalse()
    {
        var sut = CreateSut();

        var revoked = await sut.RevokeAsync("unknown-plaintext", default);

        Assert.False(revoked);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeAsync_ActiveToken_MarksRevoked()
    {
        var user = await CreateUserAsync();
        var token = await IssueTokenAsync(user.Id);
        var sut = CreateSut();

        var revoked = await sut.RevokeAsync(token.PlainTextToken, default);

        Assert.True(revoked);
        await using var verify = CreateContext();
        var row = await verify.Set<RefreshToken>()
            .AsNoTracking()
            .SingleAsync(t => t.Id == token.Entity.Id);
        Assert.True(row.IsRevoked);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeAsync_AlreadyRevokedToken_ReturnsFalse()
    {
        var user = await CreateUserAsync();
        var token = await IssueTokenAsync(user.Id);
        var sut = CreateSut();

        await sut.RevokeAsync(token.PlainTextToken, default);
        var secondCall = await sut.RevokeAsync(token.PlainTextToken, default);

        Assert.False(secondCall);
    }
}
