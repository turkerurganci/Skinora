using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Auth.Application.TosAcceptance;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class TosAcceptanceServiceTests : IntegrationTestBase
{
    static TosAcceptanceServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
    }

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 4, 21, 12, 0, 0, TimeSpan.Zero));

    private TosAcceptanceService CreateSut() => new(Context, _clock);

    private async Task<Guid> CreateUserAsync(bool alreadyAccepted = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Tester",
            TosAcceptedVersion = alreadyAccepted ? "0.9" : null,
            TosAcceptedAt = alreadyAccepted ? DateTime.UtcNow.AddDays(-1) : null,
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_ValidInput_PersistsVersionAndTimestamps()
    {
        var userId = await CreateUserAsync();

        var result = await CreateSut().AcceptAsync(userId, "1.0", ageOver18: true, default);

        Assert.Equal(_clock.GetUtcNow().UtcDateTime, result.AcceptedAt);

        await using var verify = CreateContext();
        var user = await verify.Set<User>().SingleAsync(u => u.Id == userId);
        Assert.Equal("1.0", user.TosAcceptedVersion);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, user.TosAcceptedAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, user.AgeConfirmedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_AgeOver18False_ThrowsValidationException()
    {
        var userId = await CreateUserAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateSut().AcceptAsync(userId, "1.0", ageOver18: false, default));

        Assert.Contains(ex.Errors, e => e.PropertyName == "ageOver18");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_EmptyTosVersion_ThrowsValidationException()
    {
        var userId = await CreateUserAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateSut().AcceptAsync(userId, "   ", ageOver18: true, default));

        Assert.Contains(ex.Errors, e => e.PropertyName == "tosVersion");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_TosVersionTooLong_ThrowsValidationException()
    {
        var userId = await CreateUserAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateSut().AcceptAsync(userId, new string('x', 50), ageOver18: true, default));

        Assert.Contains(ex.Errors, e => e.PropertyName == "tosVersion");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_SameVersionAlreadyAccepted_ThrowsDomainException()
    {
        // WP11 — re-accepting the EXACT version already on file is a no-op
        // duplicate → 409 (07 §4.4). CreateUserAsync(alreadyAccepted) stores "0.9".
        var userId = await CreateUserAsync(alreadyAccepted: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateSut().AcceptAsync(userId, "0.9", ageOver18: true, default));

        Assert.Equal("TOS_ALREADY_ACCEPTED", ex.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_NewVersionAfterBump_UpgradesAndPreservesAgeConfirmation()
    {
        // WP11 — a user who accepted an OLD version must be able to re-accept a
        // NEW version (T30 reprompt). The accepted version + timestamp re-stamp,
        // but the original 18+ attestation (AgeConfirmedAt) is preserved.
        var ageConfirmedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000009",
            SteamDisplayName = "Upgrader",
            TosAcceptedVersion = "0.9",
            TosAcceptedAt = DateTime.UtcNow.AddDays(-1),
            AgeConfirmedAt = ageConfirmedAt,
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();

        var result = await CreateSut().AcceptAsync(user.Id, "2.0", ageOver18: true, default);

        Assert.Equal(_clock.GetUtcNow().UtcDateTime, result.AcceptedAt);

        await using var verify = CreateContext();
        var updated = await verify.Set<User>().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("2.0", updated.TosAcceptedVersion);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, updated.TosAcceptedAt);
        Assert.Equal(ageConfirmedAt, updated.AgeConfirmedAt); // NOT re-stamped
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateSut().AcceptAsync(Guid.NewGuid(), "1.0", ageOver18: true, default));
    }
}
