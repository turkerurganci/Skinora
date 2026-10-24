using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Skinora.API.Configuration;

/// <summary>
/// T171a — the backend's shared secrets come from Docker secret files
/// (DEFERRED_BACKLOG <c>SharedSecretsInPlainEnv</c>; the T163 pattern of the
/// signing secrets, 05 §3.5, DEPLOY_RUNBOOK §L.9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Files, not variables.</b> Each secret in <see cref="Specs"/> is read
/// from <c>{SharedSecrets:Directory}/{file}</c> (default
/// <c>/run/secrets</c>, where compose mounts it) and layered over every other
/// configuration source. An environment variable is visible to
/// <c>docker inspect</c>, <c>docker compose config</c> and every child
/// process; a mounted file is not.
/// </para>
/// <para>
/// <b>The variable path is Development-only.</b> Outside the Development
/// environment (Production, Staging, a typo) a value for one of these keys
/// coming from an environment variable or the command line stops the start
/// (<c>SHARED_SECRET_IN_ENV</c>) instead of being used or quietly ignored, and
/// the file is the ONLY source: a required secret without a file stops the
/// start (<c>SHARED_SECRET_MISSING</c>), an optional one is blanked — the
/// <c>REPLACE_IN_ENV</c> placeholders of appsettings.json never reach a
/// production process. In Development the variable / appsettings path keeps
/// working and a file, when present, wins.
/// </para>
/// <para>
/// <b>Never echoed.</b> Error messages and the startup summary carry the
/// configuration key, the file path and the source — never a value.
/// </para>
/// </remarks>
public static class SharedSecretFiles
{
    /// <summary>Configuration key of the directory the secret files live in.</summary>
    public const string DirectoryKey = "SharedSecrets:Directory";

    /// <summary>Where compose mounts Docker secrets.</summary>
    public const string DefaultDirectory = "/run/secrets";

    /// <summary>The runbook's <c>openssl rand -hex 32</c> is 64 characters; the sidecars' INTERNAL_KEY floor.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>
    /// Every shared secret the backend holds, with the Docker secret file it
    /// is read from. The file names are the compose secret names (one file
    /// feeds both ends of a pair: backend + sidecar).
    /// </summary>
    public static readonly IReadOnlyList<SharedSecretSpec> Specs =
    [
        // JWT signing key (HMAC-SHA256) — backend only.
        new("Jwt:Secret", "jwt_secret", RequiredOutsideDevelopment: true, MinimumKeyLength),
        // Rotation grace key (AuthModule) — optional, used only during a rotation.
        new("Jwt:PreviousSecret", "jwt_previous_secret", RequiredOutsideDevelopment: false, MinimumKeyLength),
        // Webhook HMAC secrets — backend + the network's service (05 §3.3a).
        new("Webhook:BlockchainSharedSecret", "blockchain_webhook_secret", RequiredOutsideDevelopment: true, MinimumKeyLength),
        new("Webhook:EthereumSharedSecret", "ethereum_webhook_secret", RequiredOutsideDevelopment: false, MinimumKeyLength),
        // X-Internal-Key of each sidecar (05 §3.4) — backend + that sidecar.
        new("SteamSidecar:InternalKey", "steam_sidecar_internal_key", RequiredOutsideDevelopment: true, MinimumKeyLength),
        new("BlockchainSidecar:InternalKey", "blockchain_sidecar_internal_key", RequiredOutsideDevelopment: true, MinimumKeyLength),
        new("EthereumSidecar:InternalKey", "ethereum_sidecar_internal_key", RequiredOutsideDevelopment: false, MinimumKeyLength),
        // T165 notification channel secrets — backend only (the bot token also
        // reaches Grafana's render step through the same file). Their shape is
        // judged by NotificationChannelAvailability: blank or malformed runs the
        // channel "misconfigured" (off + alarm), so they are optional here.
        new("Resend:ApiKey", "resend_api_key", RequiredOutsideDevelopment: false, MinimumLength: 0),
        new("Resend:WebhookSigningSecret", "resend_webhook_signing_secret", RequiredOutsideDevelopment: false, MinimumLength: 0),
        new("Telegram:BotToken", "telegram_bot_token", RequiredOutsideDevelopment: false, MinimumLength: 0),
        new("Telegram:WebhookSecretToken", "telegram_webhook_secret_token", RequiredOutsideDevelopment: false, MinimumLength: 0),
    ];

    /// <summary>
    /// Reads the secret files, validates them and layers the result over
    /// <paramref name="configuration"/>. Throws
    /// <see cref="SharedSecretConfigurationException"/> (every problem at once)
    /// when the process must not start.
    /// </summary>
    public static SharedSecretReport Apply(
        IConfigurationManager configuration,
        IHostEnvironment environment,
        Func<string, string?>? readFile = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        readFile ??= ReadSecretFile;

        var envPathOpen = environment.IsDevelopment();
        var directory = configuration[DirectoryKey];
        if (string.IsNullOrWhiteSpace(directory)) directory = DefaultDirectory;

        var exposed = ((IConfigurationRoot)configuration).Providers
            .Where(p => p is EnvironmentVariablesConfigurationProvider or CommandLineConfigurationProvider)
            .ToList();

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<SharedSecretSourceEntry>();
        var problems = new List<SharedSecretProblem>();

        foreach (var spec in Specs)
        {
            var path = Path.Combine(directory, spec.FileName);
            var fromExposed = exposed.Any(p => p.TryGet(spec.ConfigKey, out var v) && !string.IsNullOrWhiteSpace(v));

            string? fromFile;
            try
            {
                fromFile = readFile(path);
            }
            catch (SharedSecretUnreadableException)
            {
                problems.Add(new(SharedSecretErrorCodes.Unreadable, spec.ConfigKey, path,
                    "exists but cannot be read as a file. A missing source file makes Docker create a DIRECTORY " +
                    "at the mount point — create the file (secrets/README.md) and remove the directory."));
                continue;
            }

            if (fromExposed && !envPathOpen)
            {
                problems.Add(new(SharedSecretErrorCodes.InEnvironment, spec.ConfigKey, path,
                    "is set as an environment variable or command-line argument. Outside Development it is read " +
                    "only from its Docker secret file — remove the variable (DEPLOY_RUNBOOK §L.9)."));
                continue;
            }

            if (fromFile is not null && fromExposed)
            {
                problems.Add(new(SharedSecretErrorCodes.Ambiguous, spec.ConfigKey, path,
                    "is provided twice (file and environment variable) — keep one."));
                continue;
            }

            if (fromFile is not null)
            {
                if (fromFile.Length < spec.MinimumLength)
                {
                    problems.Add(new(SharedSecretErrorCodes.TooShort, spec.ConfigKey, path,
                        $"must be at least {spec.MinimumLength} characters (openssl rand -hex 32)."));
                    continue;
                }
                overrides[spec.ConfigKey] = fromFile;
                sources.Add(new(spec.ConfigKey, SharedSecretSource.File));
                continue;
            }

            if (envPathOpen)
            {
                // Development: whatever the other sources say (env, appsettings, test settings).
                var current = configuration[spec.ConfigKey];
                sources.Add(new(spec.ConfigKey,
                    fromExposed ? SharedSecretSource.Environment
                    : string.IsNullOrWhiteSpace(current) ? SharedSecretSource.None
                    : SharedSecretSource.Configuration));
                continue;
            }

            if (spec.RequiredOutsideDevelopment)
            {
                problems.Add(new(SharedSecretErrorCodes.Missing, spec.ConfigKey, path,
                    "is missing or empty — the backend does not start without it (secrets/README.md)."));
                continue;
            }

            // Optional and not provisioned: blank it, so no appsettings
            // placeholder or other source stands in for the file.
            overrides[spec.ConfigKey] = string.Empty;
            sources.Add(new(spec.ConfigKey, SharedSecretSource.None));
        }

        if (problems.Count > 0)
            throw new SharedSecretConfigurationException(problems);

        if (overrides.Count > 0)
            configuration.AddInMemoryCollection(overrides);

        return new SharedSecretReport(directory, envPathOpen, sources);
    }

    /// <summary>
    /// Reads one secret file: the trimmed content, or null when the file does
    /// not exist or is blank (not provisioned). A path that exists but is not
    /// a readable file (Docker's directory for a missing source, a permission
    /// error) throws <see cref="SharedSecretUnreadableException"/>.
    /// </summary>
    public static string? ReadSecretFile(string path) => ReadSecretFile(path, File.ReadAllText);

    /// <summary>
    /// <see cref="ReadSecretFile(string)"/> with the file read supplied — the
    /// seam that lets a test raise the permission / IO error a real mount can
    /// produce (Linux <c>chmod 000</c>, a Windows sharing violation).
    /// </summary>
    public static string? ReadSecretFile(string path, Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        if (Directory.Exists(path)) throw new SharedSecretUnreadableException(path);
        string raw;
        try
        {
            raw = readAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SharedSecretUnreadableException(path);
        }
        var value = raw.Trim();
        return value.Length == 0 ? null : value;
    }
}

/// <summary>One shared secret: its configuration key and its Docker secret file.</summary>
public sealed record SharedSecretSpec(
    string ConfigKey,
    string FileName,
    bool RequiredOutsideDevelopment,
    int MinimumLength);

public enum SharedSecretSource
{
    /// <summary>Docker secret file.</summary>
    File,
    /// <summary>Environment variable / command line (Development only).</summary>
    Environment,
    /// <summary>appsettings or another non-exposed source (Development only).</summary>
    Configuration,
    /// <summary>Not provisioned.</summary>
    None,
}

public sealed record SharedSecretSourceEntry(string ConfigKey, SharedSecretSource Source);

/// <summary>What was loaded from where — logged at startup; never a value.</summary>
public sealed record SharedSecretReport(
    string Directory,
    bool EnvironmentPathOpen,
    IReadOnlyList<SharedSecretSourceEntry> Sources)
{
    public string Describe() =>
        string.Join(", ", Sources.Select(s => $"{s.ConfigKey}={s.Source.ToString().ToLowerInvariant()}"));
}

public static class SharedSecretErrorCodes
{
    public const string InEnvironment = "SHARED_SECRET_IN_ENV";
    public const string Ambiguous = "SHARED_SECRET_AMBIGUOUS";
    public const string Unreadable = "SHARED_SECRET_UNREADABLE";
    public const string Missing = "SHARED_SECRET_MISSING";
    public const string TooShort = "SHARED_SECRET_TOO_SHORT";
}

public sealed record SharedSecretProblem(string Code, string ConfigKey, string Path, string Reason)
{
    public override string ToString() => $"{Code}: {ConfigKey} ({Path}) {Reason}";
}

/// <summary>The start is refused; the message lists every problem (key, path, reason — no value).</summary>
public sealed class SharedSecretConfigurationException : InvalidOperationException
{
    public SharedSecretConfigurationException(IReadOnlyList<SharedSecretProblem> problems)
        : base("Shared secrets: the backend refused to start — "
               + string.Join("; ", problems.Select(p => p.ToString())))
    {
        Problems = problems;
    }

    public IReadOnlyList<SharedSecretProblem> Problems { get; }
}

/// <summary>A secret path exists but is not a readable file.</summary>
public sealed class SharedSecretUnreadableException(string path)
    : IOException($"Secret file {path} exists but cannot be read as a file.")
{
    public string SecretPath { get; } = path;
}
