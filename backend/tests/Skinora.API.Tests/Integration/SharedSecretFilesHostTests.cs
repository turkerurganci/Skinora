using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.API.Configuration;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T171a — the real host (Program.cs) with its shared secrets in files
/// (DEFERRED_BACKLOG <c>SharedSecretsInPlainEnv</c>). Outside Development a
/// missing, blank, short or unreadable secret file stops the start; the file
/// value is what the services see; appsettings placeholders never survive.
/// The unit twin (every rule, the environment-variable refusal) is
/// <c>Unit/Configuration/SharedSecretFilesTests</c>.
/// </summary>
public sealed class SharedSecretFilesHostTests : IClassFixture<HangfireBypassFactory>
{
    private readonly HangfireBypassFactory _factory;

    public SharedSecretFilesHostTests(HangfireBypassFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Production_With_Every_Required_File_Starts_And_Uses_The_File_Values()
    {
        using var secrets = new SharedSecretTestDirectory();
        using var host = Host(secrets, "Production");

        using var client = host.CreateClient();

        var services = host.Services;
        Assert.Equal(SharedSecretTestDirectory.RequiredValues["jwt_secret"],
            services.GetRequiredService<IOptions<JwtSettings>>().Value.Secret);
        var configuration = services.GetRequiredService<IConfiguration>();
        Assert.Equal(SharedSecretTestDirectory.RequiredValues["blockchain_webhook_secret"],
            configuration["Webhook:BlockchainSharedSecret"]);
        Assert.Equal(SharedSecretTestDirectory.RequiredValues["steam_sidecar_internal_key"],
            configuration["SteamSidecar:InternalKey"]);
        Assert.Equal(SharedSecretTestDirectory.RequiredValues["blockchain_sidecar_internal_key"],
            configuration["BlockchainSidecar:InternalKey"]);
        // Optional ones without a file are blank — appsettings.json's
        // REPLACE_IN_ENV placeholders do not reach a production process.
        Assert.Equal(string.Empty, configuration["Resend:ApiKey"]);
        Assert.Equal(string.Empty, configuration["Telegram:BotToken"]);
        Assert.Equal(string.Empty, configuration["EthereumSidecar:InternalKey"]);
    }

    [Fact]
    public void Production_Startup_Logs_Never_Carry_A_Secret_Value()
    {
        // Validation S4-1: the real startup log call in Program.cs (not only
        // SharedSecretReport.Describe) must name sources, never values.
        using var secrets = new SharedSecretTestDirectory();
        var optional = new Dictionary<string, string>
        {
            ["jwt_previous_secret"] = "t171a-test-jwt-previous-secret-0123456789ab",
            ["ethereum_webhook_secret"] = "t171a-test-eth-webhook-secret-0123456789abc",
            ["ethereum_sidecar_internal_key"] = "t171a-test-eth-internal-key-0123456789abcde",
            ["resend_api_key"] = "re_t171a_test_resend_api_key_0123456789",
            ["resend_webhook_signing_secret"] = "whsec_t171a_test_resend_signing_0123456789",
            ["telegram_bot_token"] = "123456789:t171a_test_telegram_bot_token_0123",
            ["telegram_webhook_secret_token"] = "t171a_test_telegram_webhook_token_0123456789",
        };
        foreach (var (name, value) in optional) secrets.Write(name, value + "\n");
        var sink = new CapturingLoggerProvider();
        using var host = Host(secrets, "Production", b => b.ConfigureTestServices(services =>
            services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(l =>
                l.SetMinimumLevel(LogLevel.Trace).AddProvider(sink)))));

        using var client = host.CreateClient();

        var lines = sink.Snapshot();
        var startup = Assert.Single(lines, l => l.Contains("Shared secrets loaded from", StringComparison.Ordinal));
        Assert.Contains("Jwt:Secret=file", startup);
        Assert.Contains("Telegram:BotToken=file", startup);
        foreach (var value in SharedSecretTestDirectory.RequiredValues.Values.Concat(optional.Values))
            Assert.DoesNotContain(lines, l => l.Contains(value, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("jwt_secret", "Jwt:Secret")]
    [InlineData("blockchain_webhook_secret", "Webhook:BlockchainSharedSecret")]
    [InlineData("steam_sidecar_internal_key", "SteamSidecar:InternalKey")]
    [InlineData("blockchain_sidecar_internal_key", "BlockchainSidecar:InternalKey")]
    public void Production_Without_A_Required_File_Does_Not_Start(string file, string key)
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Delete(file);

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(SharedSecretErrorCodes.Missing, problem.Code);
        Assert.Equal(key, problem.ConfigKey);
    }

    [Fact]
    public void Production_With_A_Blank_File_Does_Not_Start()
    {
        using var secrets = new SharedSecretTestDirectory();
        secrets.Write("jwt_secret", "  \n");

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(SharedSecretErrorCodes.Missing, problem.Code);
        Assert.Equal("Jwt:Secret", problem.ConfigKey);
    }

    [Fact]
    public void Production_With_A_Short_Key_Does_Not_Start_And_Does_Not_Echo_It()
    {
        using var secrets = new SharedSecretTestDirectory();
        const string shortKey = "only-31-characters-long-key-xyz";
        Assert.Equal(31, shortKey.Length);
        secrets.Write("ethereum_sidecar_internal_key", shortKey);

        var ex = AssertRefusedException(secrets, "Production");

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(SharedSecretErrorCodes.TooShort, problem.Code);
        Assert.Equal("EthereumSidecar:InternalKey", problem.ConfigKey);
        Assert.DoesNotContain(shortKey, ex.Message);
    }

    [Fact]
    public void Production_With_A_Directory_Where_The_File_Should_Be_Does_Not_Start()
    {
        // What Docker leaves at the mount point when the source file is missing.
        using var secrets = new SharedSecretTestDirectory();
        secrets.Delete("jwt_secret");
        Directory.CreateDirectory(secrets.FilePath("jwt_secret"));

        var problem = AssertRefused(secrets, "Production");

        Assert.Equal(SharedSecretErrorCodes.Unreadable, problem.Code);
        Assert.Equal("Jwt:Secret", problem.ConfigKey);
    }

    [Fact]
    public void Staging_Is_Held_To_The_Same_Rules()
    {
        using var secrets = new SharedSecretTestDirectory(withRequired: false);

        var ex = AssertRefusedException(secrets, "Staging");

        Assert.Equal(4, ex.Problems.Count(p => p.Code == SharedSecretErrorCodes.Missing));
    }

    [Fact]
    public void Development_Without_Files_Keeps_The_Configuration_Path()
    {
        using var secrets = new SharedSecretTestDirectory(withRequired: false);
        const string devSecret = "development-jwt-secret-from-settings-0123456789";
        using var host = Host(secrets, "Development", b => b.UseSetting("Jwt:Secret", devSecret));

        using var client = host.CreateClient();

        Assert.Equal(devSecret, host.Services.GetRequiredService<IOptions<JwtSettings>>().Value.Secret);
    }

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

    private SharedSecretProblem AssertRefused(SharedSecretTestDirectory secrets, string environment) =>
        Assert.Single(AssertRefusedException(secrets, environment).Problems);

    private SharedSecretConfigurationException AssertRefusedException(
        SharedSecretTestDirectory secrets, string environment)
    {
        using var host = Host(secrets, environment);
        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        var refused = ex as SharedSecretConfigurationException ?? ex.InnerException as SharedSecretConfigurationException;
        Assert.NotNull(refused);
        foreach (var value in SharedSecretTestDirectory.RequiredValues.Values)
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
}
