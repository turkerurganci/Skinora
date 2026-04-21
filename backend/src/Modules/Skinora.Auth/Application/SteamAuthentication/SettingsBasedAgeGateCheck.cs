using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// <see cref="IAgeGateCheck"/> implementation backed by the
/// <c>auth.min_steam_account_age_days</c> SystemSetting (T26 seed, default
/// 30). When the Steam account creation timestamp is unknown (private
/// profile or Steam API failure) the login is allowed — the soft gate fails
/// open so unavailable profile metadata does not lock out legitimate users.
/// </summary>
public sealed class SettingsBasedAgeGateCheck : IAgeGateCheck
{
    public const string SettingKey = "auth.min_steam_account_age_days";

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<SettingsBasedAgeGateCheck> _logger;

    public SettingsBasedAgeGateCheck(
        AppDbContext db,
        TimeProvider clock,
        ILogger<SettingsBasedAgeGateCheck> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AgeGateDecision> EvaluateAsync(
        DateTime? steamAccountCreatedAt, CancellationToken cancellationToken)
    {
        if (steamAccountCreatedAt is null)
        {
            _logger.LogDebug("Age gate skipped: Steam account creation timestamp unavailable.");
            return AgeGateDecision.Allowed();
        }

        var thresholdDays = await ReadThresholdAsync(cancellationToken);
        if (thresholdDays <= 0)
            return AgeGateDecision.Allowed();

        var ageDays = (int)(_clock.GetUtcNow().UtcDateTime - steamAccountCreatedAt.Value).TotalDays;
        if (ageDays < thresholdDays)
        {
            _logger.LogInformation(
                "Age gate blocked login: Steam account age {AgeDays}d below threshold {ThresholdDays}d.",
                ageDays, thresholdDays);
            return AgeGateDecision.Blocked(ageDays, thresholdDays);
        }

        return AgeGateDecision.Allowed();
    }

    private async Task<int> ReadThresholdAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == SettingKey && s.IsConfigured)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            ? days
            : 0;
    }
}
