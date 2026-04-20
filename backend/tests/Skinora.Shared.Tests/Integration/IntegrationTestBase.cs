using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Testcontainers.MsSql;

namespace Skinora.Shared.Tests.Integration;

/// <summary>
/// Base class for integration tests that require a real SQL Server instance.
///
/// Cross-process shared backend (T11.3): when the environment variable
/// <c>INTEGRATION_TEST_SQL_SERVER</c> is set (CI path — a single SQL Server
/// lives at the job level and is reused by every test assembly), that
/// connection string is treated as the server-level base. Otherwise a local
/// ephemeral SQL Server container is spun up once per test assembly via
/// TestContainers (local dev / fallback path).
///
/// Each test class creates its own uniquely-named database on the shared
/// server and drops it on tear-down, so parallel classes cannot collide even
/// when xUnit runs them concurrently within or across assemblies.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private const string EnvVarName = "INTEGRATION_TEST_SQL_SERVER";

    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static string? _baseConnectionString;
    private static MsSqlContainer? _fallbackContainer;

    private string _databaseName = string.Empty;

    /// <summary>
    /// The DbContext for the current test. Recreated per test via <see cref="CreateContext"/>.
    /// </summary>
    protected AppDbContext Context { get; private set; } = null!;

    /// <summary>
    /// The connection string pointing at this test class's unique database.
    /// Available after <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var baseConnectionString = await EnsureBaseConnectionStringAsync();
        _databaseName = BuildUniqueDatabaseName(GetType().Name);

        await CreateDatabaseAsync(baseConnectionString, _databaseName);

        ConnectionString = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = _databaseName,
            TrustServerCertificate = true
        }.ConnectionString;

        await using var migrationContext = CreateContext();
        // T28: apply EF Core migrations (InitialCreate+) instead of EnsureCreated
        // so the production schema-construction path — including HasData seed rows
        // — is exercised by every integration test. SQLite-backed unit tests keep
        // using EnsureCreated because the generated migrations are SQL Server
        // specific (nvarchar(max), rowversion, etc.).
        await migrationContext.Database.MigrateAsync();
        await SeedAsync(migrationContext);

        Context = CreateContext();
    }

    public async Task DisposeAsync()
    {
        if (Context is not null)
        {
            await Context.DisposeAsync();
        }

        if (_baseConnectionString is not null && !string.IsNullOrEmpty(_databaseName))
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(_baseConnectionString, _databaseName);
        }
    }

    /// <summary>
    /// Creates a new <see cref="AppDbContext"/> pointing at the test database.
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

    private static async Task<string> EnsureBaseConnectionStringAsync()
    {
        if (_baseConnectionString is not null)
        {
            return _baseConnectionString;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_baseConnectionString is not null)
            {
                return _baseConnectionString;
            }

            var envValue = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                _baseConnectionString = envValue;
            }
            else
            {
                _fallbackContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await _fallbackContainer.StartAsync();
                _baseConnectionString = _fallbackContainer.GetConnectionString();
            }
        }
        finally
        {
            _initLock.Release();
        }

        return _baseConnectionString;
    }

    private static string BuildUniqueDatabaseName(string className)
    {
        // SQL Server DB name max length is 128. Class names are typically short;
        // trim defensively, prefix with a marker, suffix with a GUID to guarantee uniqueness.
        var sanitized = className.Length > 40 ? className[..40] : className;
        var id = Guid.NewGuid().ToString("N");
        return $"T_{sanitized}_{id}";
    }

    private static async Task CreateDatabaseAsync(string baseConnectionString, string dbName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true
        };

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{dbName}]";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string dbName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
            TrustServerCertificate = true
        };

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // SINGLE_USER + ROLLBACK IMMEDIATE ensures no lingering connections block the drop.
        cmd.CommandText =
            $"IF DB_ID('{dbName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{dbName}]; END";
        await cmd.ExecuteNonQueryAsync();
    }
}
