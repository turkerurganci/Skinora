using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using StackExchange.Redis;

namespace Skinora.API.Configuration;

/// <summary>
/// T171b — the backend's third-party credentials come from Docker secret
/// files (DEFERRED_BACKLOG <c>ThirdPartyCredentialsInPlainEnv</c>; owner
/// decision 2026-10-24; the T171a pattern of <see cref="SharedSecretFiles"/>,
/// 05 §3.5, DEPLOY_RUNBOOK §L.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds.</b> A plain value (<see cref="CredentialKind.Value"/>: the
/// Steam Web API key, the Discord client secret and bot token) is read from
/// <c>{SharedSecrets:Directory}/{file}</c> and layered over every other
/// source, exactly like a shared secret. A database or Redis PASSWORD
/// (<see cref="CredentialKind.SqlServerPassword"/>,
/// <see cref="CredentialKind.RedisPassword"/>) is only one part of a
/// connection string: the rest (server, database, user, options) still comes
/// from configuration — the environment variable carries NO password — and
/// the password from the file is put into it here, so the full string exists
/// only inside this process, never in <c>docker inspect</c>.
/// </para>
/// <para>
/// <b>The variable path is Development-only.</b> Outside Development a value
/// from an environment variable or the command line (for a connection string:
/// a connection string that carries a password) stops the start
/// (<c>CREDENTIAL_IN_ENV</c>); a required file that is missing or empty stops
/// it (<c>CREDENTIAL_MISSING</c>); an optional one without a file is blanked
/// (appsettings <c>REPLACE_IN_ENV</c> placeholders never reach production).
/// A path that exists but is not a readable file — Docker's directory for a
/// missing source — is <c>CREDENTIAL_UNREADABLE</c> in every environment.
/// </para>
/// <para>
/// <b>Never echoed.</b> Messages and the startup summary carry the
/// configuration key, the file path and the source — never a value, never a
/// connection string (a parser error message could quote one).
/// </para>
/// </remarks>
public static class ThirdPartyCredentialFiles
{
    /// <summary>
    /// Every third-party credential the backend holds, with its Docker secret
    /// file. The file names are the compose secret names; the database and
    /// Redis password files are the same ones the database / Redis containers
    /// read, so the two ends cannot drift.
    /// </summary>
    public static readonly IReadOnlyList<CredentialSpec> Specs =
    [
        // SQL Server sa password — skinora-db, skinora-db-backup and the backend.
        new("ConnectionStrings:DefaultConnection", "mssql_sa_password", CredentialKind.SqlServerPassword, RequiredOutsideDevelopment: true),
        // Redis requirepass — skinora-redis, the backend and the Steam service's cache.
        new("Redis:ConnectionString", "redis_password", CredentialKind.RedisPassword, RequiredOutsideDevelopment: true),
        // Steam Web API key — the backend (OpenID profile) and the Steam service.
        // Optional: without it login still works, the profile falls back to a placeholder.
        new("SteamOpenId:WebApiKey", "steam_api_key", CredentialKind.Value, RequiredOutsideDevelopment: false),
        // Discord — the channel is off in production (Discord__Provider=disabled,
        // owner decision 2026-10-21); compose mounts neither file. Opening it
        // means mounting them (DEPLOY_RUNBOOK §L.9), never a variable.
        new("Discord:ClientSecret", "discord_client_secret", CredentialKind.Value, RequiredOutsideDevelopment: false),
        new("Discord:BotToken", "discord_bot_token", CredentialKind.Value, RequiredOutsideDevelopment: false),
    ];

    /// <summary>
    /// The characters a Redis password may use: it is written into a quoted
    /// <c>requirepass</c> line (scripts/redis/redis-entrypoint.sh), a
    /// StackExchange.Redis configuration string (comma-separated) and the
    /// Steam service's <c>redis://</c> URL. The URL-unreserved set plus
    /// <c>+</c> and <c>=</c> survives all three unescaped; <c>openssl rand -hex 32</c> fits.
    /// </summary>
    public static readonly Regex RedisPasswordCharacters = new("^[A-Za-z0-9._~+=-]+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the credential files, validates them and layers the result over
    /// <paramref name="configuration"/>. Throws
    /// <see cref="ThirdPartyCredentialConfigurationException"/> (every problem
    /// at once) when the process must not start.
    /// </summary>
    public static SharedSecretReport Apply(
        IConfigurationManager configuration,
        IHostEnvironment environment,
        Func<string, string?>? readFile = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        readFile ??= SharedSecretFiles.ReadSecretFile;

        var envPathOpen = environment.IsDevelopment();
        var directory = configuration[SharedSecretFiles.DirectoryKey];
        if (string.IsNullOrWhiteSpace(directory)) directory = SharedSecretFiles.DefaultDirectory;

        var exposed = ((IConfigurationRoot)configuration).Providers
            .Where(p => p is EnvironmentVariablesConfigurationProvider or CommandLineConfigurationProvider)
            .ToList();

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<SharedSecretSourceEntry>();
        var problems = new List<SharedSecretProblem>();

        foreach (var spec in Specs)
        {
            var path = Path.Combine(directory, spec.FileName);
            var label = spec.Kind == CredentialKind.Value ? spec.ConfigKey : $"{spec.ConfigKey} (password)";

            string? fromFile;
            try
            {
                fromFile = readFile(path);
            }
            catch (SharedSecretUnreadableException)
            {
                problems.Add(new(CredentialErrorCodes.Unreadable, label, path,
                    "exists but cannot be read as a file. A missing source file makes Docker create a DIRECTORY " +
                    "at the mount point — create the file (secrets/README.md) and remove the directory."));
                continue;
            }

            // What an exposed source (environment variable, command line) says.
            // For a connection string only its PASSWORD part counts: the rest
            // of the string legitimately comes from the environment.
            bool fromExposed;
            try
            {
                fromExposed = exposed.Any(p => p.TryGet(spec.ConfigKey, out var v)
                                               && !string.IsNullOrWhiteSpace(v)
                                               && (spec.Kind == CredentialKind.Value || CarriesPassword(spec.Kind, v)));
            }
            catch (CredentialFormatException)
            {
                problems.Add(new(CredentialErrorCodes.Invalid, label, path,
                    "comes from an environment variable that is not a valid connection string (not shown)."));
                continue;
            }

            if (fromExposed && !envPathOpen)
            {
                problems.Add(new(CredentialErrorCodes.InEnvironment, label, path,
                    spec.Kind == CredentialKind.Value
                        ? "is set as an environment variable or command-line argument. Outside Development it is read " +
                          "only from its Docker secret file — remove the variable (DEPLOY_RUNBOOK §L.9)."
                        : "— the connection string in an environment variable or command-line argument carries a " +
                          "password. Outside Development the password is read only from its Docker secret file — " +
                          "remove it from the connection string (DEPLOY_RUNBOOK §L.9)."));
                continue;
            }

            if (fromFile is not null && fromExposed)
            {
                problems.Add(new(CredentialErrorCodes.Ambiguous, label, path,
                    "is provided twice (file and environment variable) — keep one."));
                continue;
            }

            if (fromFile is not null)
            {
                if (spec.Kind == CredentialKind.Value)
                {
                    overrides[spec.ConfigKey] = fromFile;
                    sources.Add(new(label, SharedSecretSource.File));
                    continue;
                }

                if (spec.Kind == CredentialKind.RedisPassword && !RedisPasswordCharacters.IsMatch(fromFile))
                {
                    problems.Add(new(CredentialErrorCodes.Invalid, label, path,
                        "may use only A-Z a-z 0-9 and . _ ~ + = - (openssl rand -hex 32) — it is written into " +
                        "redis.conf, a connection string and a redis:// URL."));
                    continue;
                }

                // No connection string at all: nothing to put the password
                // into — the consumer reports the missing string as before.
                var baseString = configuration[spec.ConfigKey];
                if (string.IsNullOrWhiteSpace(baseString))
                {
                    sources.Add(new(label, SharedSecretSource.File));
                    continue;
                }

                try
                {
                    overrides[spec.ConfigKey] = WithPassword(spec.Kind, baseString, fromFile);
                }
                catch (CredentialFormatException)
                {
                    problems.Add(new(CredentialErrorCodes.Invalid, label, path,
                        "— the connection string it goes into is not valid (not shown)."));
                    continue;
                }
                sources.Add(new(label, SharedSecretSource.File));
                continue;
            }

            if (envPathOpen)
            {
                // Development: whatever the other sources say (env, appsettings, test settings).
                var current = configuration[spec.ConfigKey];
                sources.Add(new(label,
                    fromExposed ? SharedSecretSource.Environment
                    : string.IsNullOrWhiteSpace(current) ? SharedSecretSource.None
                    : SharedSecretSource.Configuration));
                continue;
            }

            if (spec.RequiredOutsideDevelopment)
            {
                problems.Add(new(CredentialErrorCodes.Missing, label, path,
                    "is missing or empty — the backend does not start without it (secrets/README.md)."));
                continue;
            }

            // Optional and not provisioned: blank it, so no appsettings
            // placeholder or other source stands in for the file.
            overrides[spec.ConfigKey] = string.Empty;
            sources.Add(new(label, SharedSecretSource.None));
        }

        if (problems.Count > 0)
            throw new ThirdPartyCredentialConfigurationException(problems);

        if (overrides.Count > 0)
            configuration.AddInMemoryCollection(overrides);

        return new SharedSecretReport(directory, envPathOpen, sources);
    }

    /// <summary>True when the connection string carries a non-empty password.</summary>
    public static bool CarriesPassword(CredentialKind kind, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            return kind switch
            {
                CredentialKind.SqlServerPassword =>
                    !string.IsNullOrEmpty(new SqlConnectionStringBuilder(connectionString).Password),
                CredentialKind.RedisPassword =>
                    !string.IsNullOrEmpty(ConfigurationOptions.Parse(connectionString, ignoreUnknown: true).Password),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new CredentialFormatException();
        }
    }

    /// <summary>
    /// The connection string with its password replaced by
    /// <paramref name="password"/>. Every other part is kept.
    /// </summary>
    public static string WithPassword(CredentialKind kind, string? connectionString, string password)
    {
        try
        {
            switch (kind)
            {
                case CredentialKind.SqlServerPassword:
                    {
                        var builder = new SqlConnectionStringBuilder(connectionString ?? string.Empty) { Password = password };
                        return builder.ConnectionString;
                    }
                case CredentialKind.RedisPassword:
                    {
                        var options = ConfigurationOptions.Parse(connectionString ?? string.Empty, ignoreUnknown: true);
                        options.Password = password;
                        return options.ToString(includePassword: true);
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new CredentialFormatException();
        }
    }

    /// <summary>A connection string could not be parsed; carries no text of it.</summary>
    private sealed class CredentialFormatException : Exception;
}

public enum CredentialKind
{
    /// <summary>The whole configuration value is the secret.</summary>
    Value,
    /// <summary>The Password part of a SQL Server connection string.</summary>
    SqlServerPassword,
    /// <summary>The password part of a StackExchange.Redis configuration string.</summary>
    RedisPassword,
}

/// <summary>One third-party credential: its configuration key, Docker secret file and kind.</summary>
public sealed record CredentialSpec(
    string ConfigKey,
    string FileName,
    CredentialKind Kind,
    bool RequiredOutsideDevelopment);

public static class CredentialErrorCodes
{
    public const string InEnvironment = "CREDENTIAL_IN_ENV";
    public const string Ambiguous = "CREDENTIAL_AMBIGUOUS";
    public const string Unreadable = "CREDENTIAL_UNREADABLE";
    public const string Missing = "CREDENTIAL_MISSING";
    public const string Invalid = "CREDENTIAL_INVALID";
}

/// <summary>The start is refused; the message lists every problem (key, path, reason — no value).</summary>
public sealed class ThirdPartyCredentialConfigurationException : InvalidOperationException
{
    public ThirdPartyCredentialConfigurationException(IReadOnlyList<SharedSecretProblem> problems)
        : base("Third-party credentials: the backend refused to start — "
               + string.Join("; ", problems.Select(p => p.ToString())))
    {
        Problems = problems;
    }

    public IReadOnlyList<SharedSecretProblem> Problems { get; }
}
