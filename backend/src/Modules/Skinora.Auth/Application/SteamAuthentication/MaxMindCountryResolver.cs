using System.Net;
using MaxMind.GeoIP2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// IP→country lookup backed by a MaxMind GeoLite2-Country MMDB file
/// (T83 — 02 §21.1, 03 §11a.1, 08 §8). The reader is constructed at DI
/// time, holds an open file handle for the process lifetime, and is
/// thread-safe per MaxMind.Db documentation. Fails open: unresolvable
/// IPs (private ranges, malformed strings, lookup misses) return
/// <c>null</c> so the auth pipeline can fall through to
/// <see cref="HeaderCountryResolver"/> via the chained resolver.
/// </summary>
public sealed class MaxMindCountryResolver : ICountryResolver, IDisposable
{
    private readonly DatabaseReader _reader;
    private readonly ILogger<MaxMindCountryResolver> _logger;

    public MaxMindCountryResolver(
        DatabaseReader reader,
        ILogger<MaxMindCountryResolver> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public string? ResolveCountry(HttpContext? httpContext, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return null;
        if (!IPAddress.TryParse(ipAddress, out var parsed)) return null;

        try
        {
            if (!_reader.TryCountry(parsed, out var response) || response is null)
                return null;

            var iso = response.Country.IsoCode;
            return string.IsNullOrWhiteSpace(iso) ? null : iso.ToUpperInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "MaxMind country lookup failed for IP {IpAddress}.", ipAddress);
            return null;
        }
    }

    public void Dispose() => _reader.Dispose();
}
