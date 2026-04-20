using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T28 — Initial migration verification.
///
/// Every test class that inherits <see cref="IntegrationTestBase"/> already
/// proves the "migration applies to an empty SQL Server" criterion implicitly
/// (the base calls <c>MigrateAsync</c> during InitializeAsync). This class
/// adds the targeted migration-level checks from the 11_IMPLEMENTATION_PLAN
/// doğrulama listesi that no other integration test covers: applied-migrations
/// recording, idempotency on re-run, model-vs-snapshot drift, and that every
/// model entity has a matching table in the freshly-migrated schema. Seed-row
/// content is T26's responsibility (Skinora.Platform.Tests SeedDataTests).
/// </summary>
public class InitialMigrationTests : IntegrationTestBase
{
    static InitialMigrationTests()
    {
        // Schema_ContainsAllExpectedTables compares every entity in the model
        // against the physical tables. All 10 module assemblies must be
        // registered so their IEntityTypeConfiguration<T> classes surface on
        // the model — otherwise the test would silently under-count entities.
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
        AdminModuleDbRegistration.RegisterAdminModule();
        PaymentsModuleDbRegistration.RegisterPaymentsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    public async Task AppliedMigrations_ContainsInitialCreate()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingMigrations_IsEmpty_AfterInitialApply()
    {
        var pending = await Context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrate_SecondRun_IsIdempotent()
    {
        // Base class already applied the migration once in InitializeAsync.
        // A second MigrateAsync call must be a no-op and not throw — that is
        // the contract EF relies on when deployments re-run migrations.
        var before = (await Context.Database.GetAppliedMigrationsAsync()).ToList();

        await using var secondContext = CreateContext();
        await secondContext.Database.MigrateAsync();

        var after = (await secondContext.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Model_HasNoPendingChanges()
    {
        // HasPendingModelChanges() compares the live model against the latest
        // migration snapshot. When this trips, the EF model drifted from
        // AppDbContextModelSnapshot.cs — a new migration must be added.
        Assert.False(
            Context.Database.HasPendingModelChanges(),
            "EF model has drifted from the latest migration snapshot. " +
            "Run `dotnet ef migrations add <Name>` to capture the change.");
    }

    [Fact]
    public async Task Schema_ContainsAllExpectedTables()
    {
        // Every entity registered on the model must have a matching table in
        // the freshly-migrated database. Any miss here means an entity was
        // added but the migration was not regenerated.
        var expected = Context.Model
            .GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();

        var actual = await Context.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sys.tables WHERE is_ms_shipped = 0")
            .ToListAsync();

        foreach (var table in expected)
        {
            Assert.Contains(table, actual);
        }
    }

    [Fact]
    public async Task Schema_ContainsEfMigrationsHistoryTable()
    {
        // Presence of __EFMigrationsHistory is the marker distinguishing a
        // migrated database from an EnsureCreated one.
        var exists = await Context.Database
            .SqlQueryRaw<int>(
                "SELECT CASE WHEN OBJECT_ID('__EFMigrationsHistory', 'U') IS NOT NULL " +
                "THEN 1 ELSE 0 END AS Value")
            .SingleAsync();

        Assert.Equal(1, exists);
    }
}
