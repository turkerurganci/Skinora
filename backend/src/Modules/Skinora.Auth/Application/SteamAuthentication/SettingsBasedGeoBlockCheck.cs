using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// <see cref="IGeoBlockCheck"/> implementation backed by the
/// <c>auth.banned_countries</c> SystemSetting (T26 seed, default
/// <c>NONE</c> = no country blocked) and an injected
/// <see cref="ICountryResolver"/>. 02 §21.1 / 03 §11a.1.
/// </summary>
/// <remarks>
/// <para>Fail-open semantics:</para>
/// <list type="bullet">
///   <item>Unknown country (resolver returned <c>null</c>) → allowed.</item>
///   <item>Empty / "NONE" banned list → allowed.</item>
///   <item>Banned list parse failure → allowed (conservative — misconfiguration
///   must not lock users out; admin alert path is out-of-scope for T30).</item>
/// </list>
/// </remarks>
public sealed class SettingsBasedGeoBlockCheck : IGeoBlockCheck
{
    public const string SettingKey = "auth.banned_countries";
    private const string NoneMarker = "NONE";

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICountryResolver _countryResolver;
    private readonly ILogger<SettingsBasedGeoBlockCheck> _logger;

    public SettingsBasedGeoBlockCheck(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor,
        ICountryResolver countryResolver,
        ILogger<SettingsBasedGeoBlockCheck> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _countryResolver = countryResolver;
        _logger = logger;
    }

    public async Task<GeoBlockDecision> EvaluateAsync(
        string? ipAddress, CancellationToken cancellationToken)
    {
        var country = _countryResolver.ResolveCountry(_httpContextAccessor.HttpContext, ipAddress);
        if (country is null)
        {
            _logger.LogDebug("Geo-block skipped: country could not be resolved for IP {IpAddress}.", ipAddress);
            return GeoBlockDecision.Allowed();
        }

        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == SettingKey && s.IsConfigured)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw.Trim(), NoneMarker, StringComparison.OrdinalIgnoreCase))
        {
            return GeoBlockDecision.Allowed();
        }

        var banned = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => c.Length == 2)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        if (banned.Contains(country))
        {
            _logger.LogInformation(
                "Geo-block rejected login: country {Country} is on the banned list.", country);
            return GeoBlockDecision.Blocked(country);
        }

        return GeoBlockDecision.Allowed();
    }
}
