using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Account;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Users.Tests.Unit.Account;

/// <summary>
/// WP12 (T36) — atomicity coverage for <see cref="AccountLifecycleService"/>.
/// The delete flow anonymizes the User then fans out to the notification and
/// auth anonymizers across separate SaveChanges; wrapping all three in one DB
/// transaction must roll the User anonymization back if a downstream step
/// throws, so a failure can never leave the account half-anonymized (06 §6.2).
/// Backed by a SQLite in-memory DbContext (relational → real transactions).
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountLifecycleServiceTests : IDisposable
{
    static AccountLifecycleServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
    }

    private const string DeleteConfirmation = "SİL";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public AccountLifecycleServiceTests()
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
    public async Task Delete_RollsBack_User_Anonymization_When_Downstream_Anonymizer_Throws()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000900",
            SteamDisplayName = "Victim",
            Email = "victim@example.com",
            DefaultRefundAddress = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567",
        };
        _db.Set<User>().Add(user);
        await _db.SaveChangesAsync();

        var sut = new AccountLifecycleService(
            _db,
            new FalseActiveTransactionChecker(),
            new ThrowingNotificationAnonymizer(),
            new NoopAuthAnonymizer(),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DeleteAsync(user.Id, DeleteConfirmation, CancellationToken.None));

        // Fresh context over the same connection reads the committed DB state —
        // the whole transaction rolled back, so the User keeps all its PII and
        // is not soft-deleted.
        await using var verifyCtx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var reloaded = await verifyCtx.Set<User>()
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == user.Id);

        Assert.False(reloaded.IsDeleted);
        Assert.Null(reloaded.DeletedAt);
        Assert.Equal("76561198000000900", reloaded.SteamId);
        Assert.Equal("Victim", reloaded.SteamDisplayName);
        Assert.Equal("victim@example.com", reloaded.Email);
        Assert.Equal("TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567", reloaded.DefaultRefundAddress);
    }

    [Fact]
    public async Task Delete_HappyPath_Anonymizes_User_And_Commits()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000901",
            SteamDisplayName = "Leaver",
            Email = "leaver@example.com",
        };
        _db.Set<User>().Add(user);
        await _db.SaveChangesAsync();

        var sut = new AccountLifecycleService(
            _db,
            new FalseActiveTransactionChecker(),
            new NoopNotificationAnonymizer(),
            new NoopAuthAnonymizer(),
            TimeProvider.System);

        var outcome = await sut.DeleteAsync(user.Id, DeleteConfirmation, CancellationToken.None);

        Assert.IsType<AccountDeleteOutcome.Success>(outcome);

        await using var verifyCtx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        var reloaded = await verifyCtx.Set<User>()
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == user.Id);

        Assert.True(reloaded.IsDeleted);
        Assert.NotNull(reloaded.DeletedAt);
        Assert.Equal("Deleted User", reloaded.SteamDisplayName);
        Assert.Null(reloaded.Email);
    }

    private sealed class FalseActiveTransactionChecker : IUserActiveTransactionChecker
    {
        public Task<bool> HasActiveTransactionsAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class ThrowingNotificationAnonymizer : INotificationAccountAnonymizer
    {
        public Task<NotificationAnonymizationResult> AnonymizeAsync(
            Guid userId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("notification anonymizer failed (test)");
    }

    private sealed class NoopNotificationAnonymizer : INotificationAccountAnonymizer
    {
        public Task<NotificationAnonymizationResult> AnonymizeAsync(
            Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(new NotificationAnonymizationResult(0, 0));
    }

    private sealed class NoopAuthAnonymizer : IAuthAccountAnonymizer
    {
        public Task<int> RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> AnonymizeSessionsAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
