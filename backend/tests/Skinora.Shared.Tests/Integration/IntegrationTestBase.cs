using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Testcontainers.MsSql;

namespace Skinora.Shared.Tests.Integration;

/// <summary>
/// Base class for integration tests that require a real SQL Server instance.
/// Uses TestContainers to spin up an ephemeral SQL Server container per test class.
/// EF Core migrations are applied automatically; override <see cref="SeedAsync"/> to insert test data.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private MsSqlContainer _container = null!;

    /// <summary>
    /// The DbContext for the current test. Recreated per test via <see cref="CreateContext"/>.
    /// </summary>
    protected AppDbContext Context { get; private set; } = null!;

    /// <summary>
    /// The connection string to the ephemeral SQL Server container.
    /// Available after <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Apply EF Core migrations / ensure schema is created
        await using var migrationContext = CreateContext();
        await migrationContext.Database.EnsureCreatedAsync();

        // Allow subclasses to seed test data
        await SeedAsync(migrationContext);

        // Create the context that tests will use
        Context = CreateContext();
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new <see cref="AppDbContext"/> pointing at the test container.
    /// Use this to create additional contexts when testing concurrency or isolation.
    /// </summary>
    protected AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>
    /// Override to seed test data after the schema is created.
    /// Default implementation does nothing.
    /// </summary>
    protected virtual Task SeedAsync(AppDbContext context)
    {
        return Task.CompletedTask;
    }
}
