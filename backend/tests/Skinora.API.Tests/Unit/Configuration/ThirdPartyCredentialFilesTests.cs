using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Skinora.API.Configuration;
using Skinora.API.Tests.Common;
using StackExchange.Redis;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T171b — <see cref="ThirdPartyCredentialFiles"/> rule by rule (DEFERRED_BACKLOG
/// <c>ThirdPartyCredentialsInPlainEnv</c>). The host-level twin is
/// <c>Integration/ThirdPartyCredentialFilesHostTests</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ThirdPartyCredentialFilesTests : IDisposable
{
    private const string SqlBase = "Server=skinora-db,1433;Database=Skinora;User Id=sa;TrustServerCertificate=True;";
    private const string RedisBase = "skinora-redis:6379,abortConnect=false";
    private static readonly string SqlPassword = SharedSecretTestDirectory.RequiredCredentialValues["mssql_sa_password"];
    private static readonly string RedisPassword = SharedSecretTestDirectory.RequiredCredentialValues["redis_password"];

    private readonly SharedSecretTestDirectory _secrets = new();
    private readonly string _envPrefix = "SKINORA_T171B_" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly List<string> _envNames = [];

    public void Dispose()
    {
        foreach (var name in _envNames) Environment.SetEnvironmentVariable(name, null);
        _secrets.Dispose();
    }

    [Fact]
    public void Every_Spec_Has_A_Distinct_Key_And_File_And_The_Passwords_Are_Required()
    {
        var specs = ThirdPartyCredentialFiles.Specs;
        Assert.Equal(specs.Count, specs.Select(s => s.ConfigKey).Distinct().Count());
        Assert.Equal(specs.Count, specs.Select(s => s.FileName).Distinct().Count());
        Assert.True(specs.Single(s => s.FileName == "mssql_sa_password").RequiredOutsideDevelopment);
        Assert.True(specs.Single(s => s.FileName == "redis_password").RequiredOutsideDevelopment);
        // No file name may collide with a shared secret's (one directory).
        Assert.Empty(specs.Select(s => s.FileName).Intersect(SharedSecretFiles.Specs.Select(s => s.FileName)));
    }

    [Fact]
    public void Production_Puts_The_File_Password_Into_The_Sql_Connection_String_And_Keeps_The_Rest()
    {
        SetEnv("ConnectionStrings__DefaultConnection", SqlBase);
        var configuration = Configuration();

        var report = ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        var built = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        Assert.Equal(SqlPassword, built.Password);
        Assert.Equal("skinora-db,1433", built.DataSource);
        Assert.Equal("Skinora", built.InitialCatalog);
        Assert.Equal("sa", built.UserID);
        Assert.True(built.TrustServerCertificate);
        Assert.False(report.EnvironmentPathOpen);
        Assert.Contains(report.Sources, s => s.ConfigKey == "ConnectionStrings:DefaultConnection (password)"
                                             && s.Source == SharedSecretSource.File);
    }

    [Fact]
    public void Production_Puts_The_File_Password_Into_The_Redis_Connection_String_And_Keeps_The_Rest()
    {
        SetEnv("Redis__ConnectionString", RedisBase);
        var configuration = Configuration();

        ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]!);
        Assert.Equal(RedisPassword, options.Password);
        var endpoint = Assert.IsType<DnsEndPoint>(Assert.Single(options.EndPoints));
        Assert.Equal("skinora-redis", endpoint.Host);
        Assert.Equal(6379, endpoint.Port);
        Assert.False(options.AbortOnConnectFail);
    }

    [Fact]
    public void Production_Replaces_The_Appsettings_Placeholder_Password()
    {
        var configuration = Configuration(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost,1433;Database=Skinora;User Id=sa;Password=REPLACE_IN_ENV;TrustServerCertificate=True",
            ["Redis:ConnectionString"] = "localhost:6379,password=REPLACE_IN_ENV,abortConnect=false",
        });

        ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        Assert.Equal(SqlPassword, new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")).Password);
        Assert.Equal(RedisPassword, ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]!).Password);
        Assert.DoesNotContain("REPLACE_IN_ENV", configuration.GetConnectionString("DefaultConnection"));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Pwd")]
    [InlineData("password")]
    public void Production_Refuses_A_Sql_Connection_String_Variable_That_Carries_A_Password(string keyword)
    {
        const string leaked = "an-environment-sql-password-0123";
        SetEnv("ConnectionStrings__DefaultConnection", $"{SqlBase}{keyword}={leaked};");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.InEnvironment, problem.Code);
        Assert.Equal("ConnectionStrings:DefaultConnection (password)", problem.ConfigKey);
        Assert.DoesNotContain(leaked, ex.Message);
        Assert.DoesNotContain(SqlPassword, ex.Message);
    }

    [Fact]
    public void Production_Refuses_A_Redis_Connection_String_Variable_That_Carries_A_Password()
    {
        SetEnv("Redis__ConnectionString", RedisBase + ",password=an-environment-redis-password");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.InEnvironment, problem.Code);
        Assert.Equal("Redis:ConnectionString (password)", problem.ConfigKey);
        Assert.DoesNotContain("an-environment-redis-password", ex.Message);
    }

    [Fact]
    public void Production_Refuses_A_Sql_Password_From_The_Command_Line()
    {
        var configuration = Configuration();
        configuration.AddCommandLine([$"--ConnectionStrings:DefaultConnection={SqlBase}Password=cmd-line-pass-0123"]);

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        Assert.Equal(CredentialErrorCodes.InEnvironment, Assert.Single(ex.Problems).Code);
        Assert.DoesNotContain("cmd-line-pass-0123", ex.Message);
    }

    [Theory]
    [InlineData("SteamOpenId__WebApiKey", "SteamOpenId:WebApiKey")]
    [InlineData("Discord__ClientSecret", "Discord:ClientSecret")]
    [InlineData("Discord__BotToken", "Discord:BotToken")]
    public void Production_Refuses_A_Value_Credential_From_An_Environment_Variable(string variable, string key)
    {
        SetEnv(variable, "an-environment-credential-0123456789");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.InEnvironment, problem.Code);
        Assert.Equal(key, problem.ConfigKey);
        Assert.DoesNotContain("an-environment-credential", ex.Message);
    }

    [Fact]
    public void Production_Accepts_A_Connection_String_Variable_Without_A_Password()
    {
        SetEnv("ConnectionStrings__DefaultConnection", SqlBase);
        SetEnv("Redis__ConnectionString", RedisBase);
        var configuration = Configuration();

        var report = ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        Assert.All(report.Sources.Where(s => s.ConfigKey.EndsWith("(password)", StringComparison.Ordinal)),
            s => Assert.Equal(SharedSecretSource.File, s.Source));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")]
    [InlineData("Developmentt")]
    public void Every_Environment_But_Development_Closes_The_Variable_Path(string environment)
    {
        SetEnv("SteamOpenId__WebApiKey", "an-environment-steam-key-0123456789");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env(environment)));

        Assert.Equal(CredentialErrorCodes.InEnvironment, Assert.Single(ex.Problems).Code);
    }

    [Theory]
    [InlineData("mssql_sa_password", "ConnectionStrings:DefaultConnection (password)")]
    [InlineData("redis_password", "Redis:ConnectionString (password)")]
    public void Production_Without_A_Password_File_Does_Not_Start(string file, string key)
    {
        _secrets.Delete(file);
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.Missing, problem.Code);
        Assert.Equal(key, problem.ConfigKey);
    }

    [Fact]
    public void Production_With_A_Blank_Password_File_Does_Not_Start()
    {
        _secrets.Write("mssql_sa_password", " \r\n");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        Assert.Equal(CredentialErrorCodes.Missing, Assert.Single(ex.Problems).Code);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void A_Directory_Where_The_File_Should_Be_Does_Not_Start_In_Any_Environment(string environment)
    {
        // What Docker leaves at the mount point when the source file is missing.
        _secrets.Delete("redis_password");
        Directory.CreateDirectory(_secrets.FilePath("redis_password"));
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env(environment)));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.Unreadable, problem.Code);
        Assert.Equal("Redis:ConnectionString (password)", problem.ConfigKey);
    }

    [Fact]
    public void Production_Reports_Every_Problem_At_Once()
    {
        _secrets.Delete("mssql_sa_password");
        _secrets.Delete("redis_password");
        SetEnv("Discord__BotToken", "an-environment-discord-token-0123456789");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        Assert.Equal(3, ex.Problems.Count);
        Assert.Equal(2, ex.Problems.Count(p => p.Code == CredentialErrorCodes.Missing));
        Assert.Single(ex.Problems, p => p.Code == CredentialErrorCodes.InEnvironment);
    }

    [Fact]
    public void Production_Blanks_Optional_Credentials_Without_A_File()
    {
        var configuration = Configuration(new()
        {
            ["Discord:ClientSecret"] = "REPLACE_IN_ENV",
            ["Discord:BotToken"] = "REPLACE_IN_ENV",
            ["SteamOpenId:WebApiKey"] = "an-appsettings-value",
        });

        ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        Assert.Equal(string.Empty, configuration["Discord:ClientSecret"]);
        Assert.Equal(string.Empty, configuration["Discord:BotToken"]);
        Assert.Equal(string.Empty, configuration["SteamOpenId:WebApiKey"]);
    }

    [Fact]
    public void Production_Reads_The_Steam_Key_From_Its_File()
    {
        _secrets.Write("steam_api_key", "0123456789ABCDEF0123456789ABCDEF\n");
        var configuration = Configuration(new() { ["SteamOpenId:WebApiKey"] = "" });

        ThirdPartyCredentialFiles.Apply(configuration, Env("Production"));

        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", configuration["SteamOpenId:WebApiKey"]);
    }

    [Theory]
    [InlineData("has,comma-0123456789")]
    [InlineData("has space-0123456789")]
    [InlineData("has\"quote-0123456789")]
    [InlineData("has\\backslash-012345")]
    [InlineData("has/slash-0123456789")]
    public void Production_Refuses_A_Redis_Password_With_Characters_The_Three_Readers_Cannot_Carry(string password)
    {
        _secrets.Write("redis_password", password);
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal(CredentialErrorCodes.Invalid, problem.Code);
        Assert.DoesNotContain(password, ex.Message);
    }

    [Fact]
    public void Production_Refuses_A_Malformed_Connection_String_Without_Quoting_It()
    {
        SetEnv("ConnectionStrings__DefaultConnection", "Server=x;NotAKeyword=secret-looking-0123;");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Production")));

        Assert.Equal(CredentialErrorCodes.Invalid, Assert.Single(ex.Problems).Code);
        Assert.DoesNotContain("secret-looking-0123", ex.Message);
        Assert.DoesNotContain("NotAKeyword", ex.Message);
    }

    [Fact]
    public void Development_Without_Files_Keeps_The_Connection_Strings_As_They_Are()
    {
        using var empty = new SharedSecretTestDirectory(withRequired: false);
        var devSql = SqlBase + "Password=dev-password-1;";
        SetEnv("ConnectionStrings__DefaultConnection", devSql);
        var configuration = Configuration(directory: empty.Path);

        var report = ThirdPartyCredentialFiles.Apply(configuration, Env("Development"));

        Assert.Equal(devSql, configuration.GetConnectionString("DefaultConnection"));
        Assert.True(report.EnvironmentPathOpen);
        Assert.Contains(report.Sources, s => s.ConfigKey == "ConnectionStrings:DefaultConnection (password)"
                                             && s.Source == SharedSecretSource.Environment);
    }

    [Fact]
    public void Development_File_And_A_Password_In_The_Variable_Together_Is_Ambiguous()
    {
        SetEnv("ConnectionStrings__DefaultConnection", SqlBase + "Password=dev-password-1;");
        var configuration = Configuration();

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(
            () => ThirdPartyCredentialFiles.Apply(configuration, Env("Development")));

        Assert.Equal(CredentialErrorCodes.Ambiguous, Assert.Single(ex.Problems).Code);
        Assert.DoesNotContain("dev-password-1", ex.Message);
    }

    [Fact]
    public void Development_With_A_File_Puts_It_Into_A_Variable_Without_A_Password()
    {
        // The docker-compose.yml development stack: ASPNETCORE_ENVIRONMENT=Development, files mounted.
        SetEnv("ConnectionStrings__DefaultConnection", SqlBase);
        var configuration = Configuration();

        ThirdPartyCredentialFiles.Apply(configuration, Env("Development"));

        Assert.Equal(SqlPassword, new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection")).Password);
    }

    [Fact]
    public void Development_Reads_A_Value_Credential_From_The_Variable()
    {
        SetEnv("SteamOpenId__WebApiKey", "dev-steam-key");
        var configuration = Configuration();

        var report = ThirdPartyCredentialFiles.Apply(configuration, Env("Development"));

        Assert.Equal("dev-steam-key", configuration["SteamOpenId:WebApiKey"]);
        Assert.Contains(report.Sources, s => s.ConfigKey == "SteamOpenId:WebApiKey" && s.Source == SharedSecretSource.Environment);
    }

    [Fact]
    public void The_Report_Names_Sources_Never_Values()
    {
        _secrets.Write("steam_api_key", "steam-key-value-0123456789");
        SetEnv("ConnectionStrings__DefaultConnection", SqlBase);
        var configuration = Configuration();

        var described = ThirdPartyCredentialFiles.Apply(configuration, Env("Production")).Describe();

        Assert.Contains("ConnectionStrings:DefaultConnection (password)=file", described);
        Assert.Contains("SteamOpenId:WebApiKey=file", described);
        Assert.Contains("Discord:BotToken=none", described);
        foreach (var value in new[] { SqlPassword, RedisPassword, "steam-key-value-0123456789", "skinora-db" })
            Assert.DoesNotContain(value, described);
    }

    [Fact]
    public void A_Permission_Error_On_A_File_Is_Unreadable_Not_Missing()
    {
        var configuration = Configuration();
        var unreadable = _secrets.FilePath("mssql_sa_password");

        var ex = Assert.Throws<ThirdPartyCredentialConfigurationException>(() => ThirdPartyCredentialFiles.Apply(
            configuration,
            Env("Production"),
            path => SharedSecretFiles.ReadSecretFile(path, p => p == unreadable
                ? throw new UnauthorizedAccessException("denied")
                : File.ReadAllText(p))));

        Assert.Equal(CredentialErrorCodes.Unreadable, Assert.Single(ex.Problems).Code);
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
