using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.API.Startup;

/// <summary>
/// T166 — reports at startup whether the Steam account age gate
/// (<c>auth.min_steam_account_age_days</c>, 02 §21.1) is at its production
/// value, and warns loudly outside Development when it is not.
/// </summary>
/// <remarks>
/// <para>
/// The repository's production value is already right: the seed and the T30
/// migration write <c>30</c> on a fresh database. What is NOT guarded is a
/// database that was lowered by hand — the local rehearsal database has run
/// with <c>1</c> since 2026-08-29 so a fresh Steam account could sign in
/// (DEPLOY_RUNBOOK §G.5). A value set through the admin API survives every
/// redeploy and nothing reported it: the gate simply stopped deterring burner
/// accounts. Same lesson as <c>PriceDeviationConfigDiagnosticHook</c> — a
/// control that is quietly weakened has to say so at boot.
/// </para>
/// <para>
/// Diagnostic only, never throws: a lower value can be a deliberate,
/// audit-logged admin decision (AD9), and the rehearsal stack runs with the
/// <c>Production</c> environment. Runs after <c>SettingsBootstrapHook</c>.
/// </para>
/// </remarks>
public sealed class AgeGateConfigDiagnosticHook : IHostedService
{
    /// <summary>Production value — 02 §21.1 "default 30 gün", seed + T30 migration.</summary>
    public const int ProductionMinimumDays = 30;

    public enum Posture
    {
        /// <summary>At or above the production value.</summary>
        Production,

        /// <summary>Enabled but below the production value (e.g. the rehearsal's 1).</summary>
        BelowProduction,

        /// <summary><c>0</c> (or negative) — the gate is switched off.</summary>
        Disabled,

        /// <summary>Row missing / unparseable — the gate reads it as 0 and stays open.</summary>
        Unreadable,
    }

    public static Posture Evaluate(string? rawValue)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            return Posture.Unreadable;
        if (days <= 0) return Posture.Disabled;
        return days < ProductionMinimumDays ? Posture.BelowProduction : Posture.Production;
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AgeGateConfigDiagnosticHook> _logger;

    public AgeGateConfigDiagnosticHook(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<AgeGateConfigDiagnosticHook> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string? raw;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            raw = await db.Set<SystemSetting>()
                .AsNoTracking()
                .Where(s => s.Key == SettingsBasedAgeGateCheck.SettingKey && s.IsConfigured)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Diagnostics must never be the reason a host fails to start.
            _logger.LogWarning(ex,
                "Age gate diagnostic could not read {Key}.", SettingsBasedAgeGateCheck.SettingKey);
            return;
        }

        var posture = Evaluate(raw);
        if (posture == Posture.Production)
        {
            _logger.LogInformation(
                "Age gate at production value — {Key}={Value} (minimum {Minimum}).",
                SettingsBasedAgeGateCheck.SettingKey, raw, ProductionMinimumDays);
            return;
        }

        if (_environment.IsDevelopment())
        {
            _logger.LogInformation(
                "Age gate {Posture} in Development — {Key}={Value} (production {Minimum}).",
                posture, SettingsBasedAgeGateCheck.SettingKey, raw ?? "<missing>", ProductionMinimumDays);
            return;
        }

        _logger.LogWarning(
            "Age gate {Posture} — {Key}={Value}, production value is {Minimum} (02 §21.1). "
            + "Fresh Steam accounts below {Minimum} days can sign in. If this is not a deliberate "
            + "admin decision, restore it via AD9 (DEPLOY_RUNBOOK §C).",
            posture, SettingsBasedAgeGateCheck.SettingKey, raw ?? "<missing>", ProductionMinimumDays);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
