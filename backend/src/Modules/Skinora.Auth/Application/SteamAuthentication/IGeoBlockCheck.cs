namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Pipeline hook for geo-block evaluation — the real implementation lands in
/// T30/T83 (IP geolocation + admin-managed banned country list).
/// <see cref="AllowAllGeoBlockCheck"/> is the default no-op used until then.
/// </summary>
public interface IGeoBlockCheck
{
    Task<GeoBlockDecision> EvaluateAsync(string? ipAddress, CancellationToken cancellationToken);
}

public sealed record GeoBlockDecision(bool IsBlocked, string? CountryCode)
{
    public static GeoBlockDecision Allowed() => new(false, null);
    public static GeoBlockDecision Blocked(string countryCode) => new(true, countryCode);
}

/// <summary>
/// Default no-op implementation: always allows the login. Replaced in T30/T83
/// by the real geo-block service.
/// </summary>
public sealed class AllowAllGeoBlockCheck : IGeoBlockCheck
{
    public Task<GeoBlockDecision> EvaluateAsync(string? ipAddress, CancellationToken cancellationToken)
        => Task.FromResult(GeoBlockDecision.Allowed());
}
