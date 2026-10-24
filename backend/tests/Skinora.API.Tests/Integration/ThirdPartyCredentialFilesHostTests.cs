using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skinora.API.Configuration;
using Skinora.API.Tests.Common;
using Skinora.Shared.Persistence;
using StackExchange.Redis;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T171b — the real host (Program.cs) with its third-party credentials in
/// files (DEFERRED_BACKLOG <c>ThirdPartyCredentialsInPlainEnv</c>). Outside
/// Development a missing, blank or unreadable database / Redis password file
/// stops the start; the file password is what the connection strings carry;
/// no value reaches the log. The unit twin (every rule, the
/// environment-variable refusal) is <c>Unit/Configuration/ThirdPartyCredentialFilesTests</c>.
/// </summary>
public sealed class ThirdPartyCredentialFilesHostTests : IClassFixture<HangfireBypassFactory>
{
    private static readonly string SqlPassword = SharedSecretTestDirectory.RequiredCredentialValues["mssql_sa_password"];
    private static readonly string RedisPassword = SharedSecretTestDirectory.RequiredCredentialValues["redis_password"];

    private readonly HangfireBypassFactory _factory;

    public ThirdPartyCredentialFilesHostTests(HangfireBypassFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Production_With_The_Password_Files_Starts_And_The_Connection_Strings_Carry_Them()
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Write("steam_api_key", "T171BTESTSTEAMKEY0123456789ABCDEF\n");
        using var host = Host(secrets, "Production");

        using var client = host.CreateClient();

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var sql = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        Assert.Equal(SqlPassword, sql.Password);
        Assert.Equal("SkinoraTest", sql.InitialCatalog); // HangfireBypassFactory's string — the rest is kept
        Assert.Equal(RedisPassword, ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]!).Password);
        Assert.Equal("T171BTESTSTEAMKEY0123456789ABCDEF", configuration["SteamOpenId:WebApiKey"]);
        // Optional ones without a file are blank — appsettings.json's
        // REPLACE_IN_ENV placeholders do not reach a production process.
        Assert.Equal(string.Empty, configuration["Discord:ClientSecret"]);
        Assert.Equal(string.Empty, configuration["Discord:BotToken"]);
    }

    [Fact]
    public void Production_Startup_Logs_Never_Carry_A_Credential_Value()
    {
        using var secrets = new SharedSecretTestDirectory();
        var optional = new Dictionary<string, string>
        {
            ["steam_api_key"] = "T171BTESTSTEAMKEY0123456789ABCDEF",
            ["discord_client_secret"] = "t171b-test-discord-client-secret-0123",
            ["discord_bot_token"] = "t171b-test-discord-bot-token-0123456789",
        };
        foreach (var (name, value) in optional) secrets.Write(name, value + "\n");
        var sink = new CapturingLoggerProvider();
        using var host = Host(secrets, "Production", b => b.ConfigureTestServices(services =>
            services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(l =>
                l.SetMinimumLevel(LogLevel.Trace).AddProvider(sink)))));

        using var client = host.CreateClient();

        var lines = sink.Snapshot();
        var startup = Assert.Single(lines, l => l.Contains("Third-party credentials loaded from", StringComparison.Ordinal));
        Assert.Contains("ConnectionStrings:DefaultConnection (password)=file", startup);
        Assert.Contains("Redis:ConnectionString (password)=file", startup);
        Assert.Contains("SteamOpenId:WebApiKey=file", startup);
        Assert.Contains("environment path closed", startup);
        foreach (var value in SharedSecretTestDirectory.RequiredCredentialValues.Values.Concat(optional.Values))
            Assert.DoesNotContain(lines, l => l.Contains(value, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("mssql_sa_password", "ConnectionStrings:DefaultConnection (password)")]
    [InlineData("redis_password", "Redis:ConnectionString (password)")]
    public void Production_Without_A_Password_File_Does_Not_Start(string file, string key)
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Delete(file);

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(CredentialErrorCodes.Missing, problem.Code);
        Assert.Equal(key, problem.ConfigKey);
    }

    [Fact]
    public void Production_With_A_Blank_Password_File_Does_Not_Start()
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Write("redis_password", "\n");

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(CredentialErrorCodes.Missing, problem.Code);
    }

    [Fact]
    public void Production_With_A_Directory_Where_The_File_Should_Be_Does_Not_Start()
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Delete("mssql_sa_password");
        Directory.CreateDirectory(secrets.FilePath("mssql_sa_password"));

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(CredentialErrorCodes.Unreadable, problem.Code);
        Assert.Equal("ConnectionStrings:DefaultConnection (password)", problem.ConfigKey);
    }

    [Fact]
    public void Staging_Is_Held_To_The_Same_Rules()
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Delete("mssql_sa_password");
        secrets.Delete("redis_password");

        using var host = Host(secrets, "Staging");
        var refused = Refusal(() => host.CreateClient());

        Assert.Equal(2, refused.Problems.Count(p => p.Code == CredentialErrorCodes.Missing));
    }

    [Fact]
    public void Development_Without_Files_Keeps_The_Configured_Connection_String()
    {
        using var secrets = new SharedSecretTestDirectory(withRequired: false);
        const string devSql = "Server=localhost,1433;Database=SkinoraDev;User Id=sa;Password=dev-only-1;TrustServerCertificate=True";
        using var host = Host(secrets, "Development", b => b.UseSetting("ConnectionStrings:DefaultConnection", devSql));

        using var client = host.CreateClient();

        Assert.Equal("dev-only-1", new SqlConnectionStringBuilder(
            host.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")).Password);
    }

    private SharedSecretProblem AssertRefused(SharedSecretTestDirectory secrets, string environment)
    {
        using var host = Host(secrets, environment);
        return Assert.Single(Refusal(() => host.CreateClient()).Problems);
    }

    private static ThirdPartyCredentialConfigurationException Refusal(Action start)
    {
        var ex = Assert.ThrowsAny<Exception>(start);
        var refused = ex as ThirdPartyCredentialConfigurationException
                      ?? ex.InnerException as ThirdPartyCredentialConfigurationException;
        Assert.NotNull(refused);
        foreach (var value in SharedSecretTestDirectory.RequiredCredentialValues.Values)
            Assert.DoesNotContain(value, refused.Message);
        return refused;
    }

    private WebApplicationFactory<Program> Host(
        SharedSecretTestDirectory secrets,
        string environment,
        Action<IWebHostBuilder>? configure = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(SharedSecretFiles.DirectoryKey, secrets.Path);
            configure?.Invoke(builder);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options => options.UseSqlite("DataSource=:memory:"));
            });
        });

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _lines = [];

        public List<string> Snapshot()
        {
            lock (_lines) return [.. _lines];
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._lines) owner._lines.Add($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
