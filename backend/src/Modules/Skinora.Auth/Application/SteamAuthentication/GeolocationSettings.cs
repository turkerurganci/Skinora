namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Configuration for the MaxMind GeoLite2 IP→country resolver (T83). The
/// MMDB file is licensed for redistribution restrictions and is NOT
/// committed to the repository — operators download it from MaxMind and
/// mount it at <see cref="DatabasePath"/>. When <see cref="DatabasePath"/>
/// is empty or the file is missing, the resolver fails open and the chain
/// falls back to <see cref="HeaderCountryResolver"/>.
/// </summary>
public sealed class GeolocationSettings
{
    public const string SectionName = "Geolocation";

    /// <summary>
    /// Absolute path to the GeoLite2-Country MMDB file. Empty in dev /
    /// staging until ops provisions the file; geo-block then defers to the
    /// edge-set <c>X-Country-Code</c> header.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;
}
