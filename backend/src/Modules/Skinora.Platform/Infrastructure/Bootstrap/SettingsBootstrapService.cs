using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Settings;
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
/// Validation (T41): every hydrated value runs through
/// <see cref="SystemSettingsValidator"/> — same type + range + cross-key
/// pipeline used by the admin update API. This way an admin-blocked value
/// (e.g. <c>commission_rate = 1.5</c>) cannot sneak in via env var, and
/// cross-key invariants (<c>payment_timeout_min &lt; payment_timeout_max</c>,
/// monitoring polling order) are enforced at boot before any feature
/// reads them.
/// </para>
/// </remarks>
public class SettingsBootstrapService
{
    private const string EnvPrefix = "SKINORA_SETTING_";

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsBootstrapService> _logger;
    private readonly SystemSettingsValidator _validator;

    public SettingsBootstrapService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<SettingsBootstrapService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _validator = SystemSettingsValidator.Instance;
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

            var single = _validator.ValidateSingle(setting.Key, envValue, setting.DataType);
            if (!single.IsValid)
            {
                throw new InvalidOperationException(
                    $"SystemSetting '{setting.Key}' env override '{envValue}' " +
                    $"failed validation: {single.ErrorMessage}");
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

        // Cross-key invariants run on the post-hydration snapshot. A failure
        // at this stage is a configuration mismatch rather than a single-row
        // typo (e.g. payment_timeout_min was hydrated to 60 but max was left
        // at 30 from a previous admin update).
        var allRows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var cross = _validator.ValidateCrossKey(allRows);
        if (!cross.IsValid)
        {
            throw new InvalidOperationException(
                $"Startup fail-fast: SystemSetting cross-key invariant violated — {cross.ErrorMessage}");
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
}
