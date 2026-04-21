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
    public async Task AcceptAsync_AlreadyAccepted_ThrowsDomainException()
    {
        var userId = await CreateUserAsync(alreadyAccepted: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateSut().AcceptAsync(userId, "1.0", ageOver18: true, default));

        Assert.Equal("TOS_ALREADY_ACCEPTED", ex.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AcceptAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateSut().AcceptAsync(Guid.NewGuid(), "1.0", ageOver18: true, default));
    }
}
