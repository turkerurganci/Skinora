using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Skinora.API.Configuration;
using Skinora.API.Tests.Common;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T171a — <see cref="SharedSecretFiles"/> rule by rule (DEFERRED_BACKLOG
/// <c>SharedSecretsInPlainEnv</c>). The host-level twin is
/// <c>Integration/SharedSecretFilesHostTests</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SharedSecretFilesTests : IDisposable
{
    private readonly SharedSecretTestDirectory _secrets = new();
    private readonly string _envPrefix = "SKINORA_T171A_" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly List<string> _envNames = [];

    public void Dispose()
    {
        foreach (var name in _envNames) Environment.SetEnvironmentVariable(name, null);
        _secrets.Dispose();
    }

    [Fact]
    public void Every_Spec_Has_A_Distinct_Key_And_File()
    {
        Assert.Equal(SharedSecretFiles.Specs.Count, SharedSecretFiles.Specs.Select(s => s.ConfigKey).Distinct().Count());
        Assert.Equal(SharedSecretFiles.Specs.Count, SharedSecretFiles.Specs.Select(s => s.FileName).Distinct().Count());
        // The six of the backlog row carry the 32-character floor.
        foreach (var key in new[]
                 {
                     "Jwt:Secret", "Webhook:BlockchainSharedSecret", "Webhook:EthereumSharedSecret",
                     "SteamSidecar:InternalKey", "BlockchainSidecar:InternalKey", "EthereumSidecar:InternalKey",
                 })
        {
            Assert.Equal(32, SharedSecretFiles.Specs.Single(s => s.ConfigKey == key).MinimumLength);
        }
    }

    [Fact]
    public void Production_Layers_The_File_Values_Over_Appsettings()
    {
        var configuration = Configuration(new() { ["Jwt:Secret"] = "REPLACE_IN_ENV_minimum_32_characters" });

        var report = SharedSecretFiles.Apply(configuration, Env("Production"));

        Assert.Equal(SharedSecretTestDirectory.RequiredValues["jwt_secret"], configuration["Jwt:Secret"]);
        Assert.False(report.EnvironmentPathOpen);
        Assert.Contains(report.Sources, s => s.ConfigKey == "Jwt:Secret" && s.Source == SharedSecretSource.File);
    }

    [Fact]
    public void Production_Refuses_A_Secret_From_An_Environment_Variable()
    {
        SetEnv("Jwt__Secret", "an-environment-jwt-secret-0123456789abcdef");
        _secrets.Delete("jwt_secret");
        var configuration = Configuration();

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(SharedSecretErrorCodes.InEnvironment, problem.Code);
        Assert.Equal("Jwt:Secret", problem.ConfigKey);
        Assert.DoesNotContain("an-environment-jwt-secret", ex.Message);
    }

    [Fact]
    public void Production_Refuses_The_Variable_Even_When_The_File_Is_There()
    {
        SetEnv("BlockchainSidecar__InternalKey", "an-environment-internal-key-0123456789abcd");
        var configuration = Configuration();

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env("Production")));

        Assert.Equal(SharedSecretErrorCodes.InEnvironment, Assert.Single(ex.Problems).Code);
    }

    [Fact]
    public void Production_Refuses_A_Secret_From_The_Command_Line()
    {
        var configuration = Configuration();
        configuration.AddCommandLine(["--Telegram:BotToken=123456:command-line-token"]);

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env("Production")));

        Assert.Equal("Telegram:BotToken", Assert.Single(ex.Problems).ConfigKey);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")]
    [InlineData("Developmentt")]
    public void Every_Environment_But_Development_Closes_The_Variable_Path(string environment)
    {
        SetEnv("Webhook__EthereumSharedSecret", "an-environment-eth-webhook-0123456789abcdef");
        var configuration = Configuration();

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env(environment)));

        Assert.Equal(SharedSecretErrorCodes.InEnvironment, Assert.Single(ex.Problems).Code);
    }

    [Fact]
    public void Development_Reads_The_Variable_When_There_Is_No_File()
    {
        _secrets.Delete("jwt_secret");
        SetEnv("Jwt__Secret", "development-env-jwt-secret-0123456789abcdef");
        var configuration = Configuration();

        var report = SharedSecretFiles.Apply(configuration, Env("Development"));

        Assert.Equal("development-env-jwt-secret-0123456789abcdef", configuration["Jwt:Secret"]);
        Assert.True(report.EnvironmentPathOpen);
        Assert.Contains(report.Sources, s => s.ConfigKey == "Jwt:Secret" && s.Source == SharedSecretSource.Environment);
    }

    [Fact]
    public void Development_Keeps_Short_Development_Values_Of_Other_Sources()
    {
        // appsettings.Development.json carries "dev-internal-key" — the
        // 32-character floor applies to files, which is what deployments use.
        var configuration = Configuration(new() { ["SteamSidecar:InternalKey"] = "dev-internal-key" });
        _secrets.Delete("steam_sidecar_internal_key");

        SharedSecretFiles.Apply(configuration, Env("Development"));

        Assert.Equal("dev-internal-key", configuration["SteamSidecar:InternalKey"]);
    }

    [Fact]
    public void Development_File_And_Variable_Together_Is_Ambiguous()
    {
        SetEnv("Jwt__Secret", "development-env-jwt-secret-0123456789abcdef");
        var configuration = Configuration();

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env("Development")));

        Assert.Equal(SharedSecretErrorCodes.Ambiguous, Assert.Single(ex.Problems).Code);
    }

    [Fact]
    public void Development_Does_Not_Require_Files()
    {
        using var empty = new SharedSecretTestDirectory(withRequired: false);
        var configuration = Configuration(directory: empty.Path);

        var report = SharedSecretFiles.Apply(configuration, Env("Development"));

        Assert.All(report.Sources, s => Assert.NotEqual(SharedSecretSource.File, s.Source));
    }

    [Fact]
    public void Production_Reports_Every_Problem_At_Once()
    {
        using var empty = new SharedSecretTestDirectory(withRequired: false);
        empty.Write("ethereum_webhook_secret", "short");
        var configuration = Configuration(directory: empty.Path);

        var ex = Assert.Throws<SharedSecretConfigurationException>(
            () => SharedSecretFiles.Apply(configuration, Env("Production")));

        Assert.Equal(
            ["Jwt:Secret", "Webhook:BlockchainSharedSecret", "Webhook:EthereumSharedSecret", "SteamSidecar:InternalKey", "BlockchainSidecar:InternalKey"],
            ex.Problems.Select(p => p.ConfigKey).ToArray());
        Assert.Equal(SharedSecretErrorCodes.TooShort, ex.Problems.Single(p => p.ConfigKey == "Webhook:EthereumSharedSecret").Code);
    }

    [Fact]
    public void Production_Blanks_Optional_Secrets_Without_A_File()
    {
        var configuration = Configuration(new()
        {
            ["Resend:ApiKey"] = "REPLACE_IN_ENV",
            ["Telegram:WebhookSecretToken"] = "REPLACE_IN_ENV",
        });

        SharedSecretFiles.Apply(configuration, Env("Production"));

        Assert.Equal(string.Empty, configuration["Resend:ApiKey"]);
        Assert.Equal(string.Empty, configuration["Telegram:WebhookSecretToken"]);
    }

    [Fact]
    public void Notification_Secrets_Are_Read_From_Files_Without_A_Length_Floor()
    {
        _secrets.Write("telegram_bot_token", "123456:ABC\n");
        var configuration = Configuration();

        SharedSecretFiles.Apply(configuration, Env("Production"));

        Assert.Equal("123456:ABC", configuration["Telegram:BotToken"]);
    }

    [Fact]
    public void The_Report_Names_Sources_Never_Values()
    {
        var configuration = Configuration();

        var described = SharedSecretFiles.Apply(configuration, Env("Production")).Describe();

        Assert.Contains("Jwt:Secret=file", described);
        Assert.Contains("Resend:ApiKey=none", described);
        foreach (var value in SharedSecretTestDirectory.RequiredValues.Values)
            Assert.DoesNotContain(value, described);
    }

    [Fact]
    public void ReadSecretFile_Missing_Is_Null_And_A_Directory_Throws()
    {
        Assert.Null(SharedSecretFiles.ReadSecretFile(_secrets.FilePath("does_not_exist")));
        Directory.CreateDirectory(_secrets.FilePath("a_directory"));
        Assert.Throws<SharedSecretUnreadableException>(
            () => SharedSecretFiles.ReadSecretFile(_secrets.FilePath("a_directory")));
        _secrets.Write("padded", "\n  value-with-whitespace  \r\n");
        Assert.Equal("value-with-whitespace", SharedSecretFiles.ReadSecretFile(_secrets.FilePath("padded")));
    }

    private ConfigurationManager Configuration(Dictionary<string, string?>? settings = null, string? directory = null)
    {
        var configuration = new ConfigurationManager();
        var all = new Dictionary<string, string?>(settings ?? [])
        {
            [SharedSecretFiles.DirectoryKey] = directory ?? _secrets.Path,
        };
        configuration.AddInMemoryCollection(all);
        configuration.AddEnvironmentVariables(_envPrefix);
        return configuration;
    }

    private void SetEnv(string name, string value)
    {
        var full = _envPrefix + name;
        _envNames.Add(full);
        Environment.SetEnvironmentVariable(full, value);
    }

    private static IHostEnvironment Env(string name) => new FakeHostEnvironment { EnvironmentName = name };

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Skinora.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
