using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Platform.Infrastructure.Bootstrap;

/// <summary>
/// Hydrates unconfigured <see cref="SystemSetting"/> rows from environment
/// variables and fail-fasts when mandatory parameters remain unset
/// (06 §8.9).
/// </summary>
/// <remarks>
/// <para>
/// Env var contract: each key <c>k</c> is looked up as
/// <c>SKINORA_SETTING_{k.ToUpperInvariant()}</c>. Only rows with
/// <c>IsConfigured = false</c> are touched — env vars never override an
/// admin-configured value, per the doc's security note.
/// </para>
/// <para>
/// Validation: the env-supplied string must parse against the row's
/// <c>DataType</c> (int, decimal, bool, string). A parse failure aborts
/// startup — silently ignoring it would leave a malformed value on a
/// parameter that downstream code relies on.
/// </para>
/// </remarks>
public class SettingsBootstrapService
{
    private const string EnvPrefix = "SKINORA_SETTING_";

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsBootstrapService> _logger;

    public SettingsBootstrapService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<SettingsBootstrapService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var unconfigured = await _db.Set<SystemSetting>()
            .Where(s => !s.IsConfigured)
            .ToListAsync(cancellationToken);

        foreach (var setting in unconfigured)
        {
            var envValue = LookupEnvValue(setting.Key);
            if (envValue is null) continue;

            if (!TryValidate(envValue, setting.DataType, out var reason))
            {
                throw new InvalidOperationException(
                    $"SystemSetting '{setting.Key}' env override '{envValue}' " +
                    $"failed {setting.DataType} validation: {reason}.");
            }

            setting.Value = envValue;
            setting.IsConfigured = true;
        }

        if (unconfigured.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        var stillMissing = await _db.Set<SystemSetting>()
            .Where(s => !s.IsConfigured)
            .Select(s => s.Key)
            .OrderBy(k => k)
            .ToListAsync(cancellationToken);

        if (stillMissing.Count > 0)
        {
            throw new InvalidOperationException(
                "Startup fail-fast: the following SystemSetting parameters are " +
                $"not configured: {string.Join(", ", stillMissing)}. Set each " +
                $"via admin UI or '{EnvPrefix}KEY_UPPER' env var and restart.");
        }

        _logger.LogInformation(
            "SystemSetting bootstrap complete — {Hydrated} env-hydrated, all required parameters configured.",
            unconfigured.Count(s => s.IsConfigured));
    }

    private string? LookupEnvValue(string key)
    {
        // IConfiguration is case-insensitive for env var lookups and wins over
        // raw Environment.GetEnvironmentVariable because tests inject config
        // through in-memory providers.
        return _configuration[EnvPrefix + key.ToUpperInvariant()];
    }

    private static bool TryValidate(string value, string dataType, out string reason)
    {
        reason = string.Empty;
        switch (dataType)
        {
            case "int":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    reason = "not an integer";
                    return false;
                }
                return true;

            case "decimal":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    reason = "not a decimal";
                    return false;
                }
                return true;

            case "bool":
                if (!bool.TryParse(value, out _))
                {
                    reason = "not 'true' or 'false'";
                    return false;
                }
                return true;

            case "string":
                if (string.IsNullOrEmpty(value))
                {
                    reason = "empty string";
                    return false;
                }
                return true;

            default:
                reason = $"unknown DataType '{dataType}'";
                return false;
        }
    }
}
