using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Tor exit node detector (T83 — 02 §21.1 supportive signal). Fetches
/// <c>torbulkexitlist</c> from <see cref="VpnDetectionSettings.TorExitListUrl"/>
/// (default torproject.org), caches the parsed <see cref="IPAddress"/>
/// hashset in memory for <see cref="VpnDetectionSettings.CacheDuration"/>
/// (default 1 hour — matches torproject's publish cadence), and returns
/// true when the lookup IP appears on the list. Soft failure: every
/// exception path returns false so a network blip never blocks login.
/// </summary>
public sealed class TorExitNodeVpnDetector : IVpnProxyDetector
{
    private readonly HttpClient _httpClient;
    private readonly VpnDetectionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TorExitNodeVpnDetector> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private HashSet<IPAddress>? _cache;
    private DateTimeOffset _cacheLoadedAt;

    public TorExitNodeVpnDetector(
        HttpClient httpClient,
        IOptions<VpnDetectionSettings> settings,
        TimeProvider timeProvider,
        ILogger<TorExitNodeVpnDetector> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> IsVpnOrProxyAsync(string? ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return false;
        if (!IPAddress.TryParse(ipAddress, out var parsed)) return false;

        var snapshot = await GetOrRefreshAsync(cancellationToken);
        return snapshot is not null && snapshot.Contains(parsed);
    }

    private async Task<HashSet<IPAddress>?> GetOrRefreshAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cache is not null && now - _cacheLoadedAt < _settings.CacheDuration)
            return _cache;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_cache is not null && now - _cacheLoadedAt < _settings.CacheDuration)
                return _cache;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_settings.RefreshTimeout);

            using var response = await _httpClient.GetAsync(_settings.TorExitListUrl, cts.Token);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            var parsed = new HashSet<IPAddress>();
            foreach (var line in body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                if (IPAddress.TryParse(trimmed, out var ip)) parsed.Add(ip);
            }

            _cache = parsed;
            _cacheLoadedAt = now;
            _logger.LogInformation(
                "Tor exit node list refreshed: {Count} entries cached for {Duration}.",
                parsed.Count, _settings.CacheDuration);
            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Tor exit list refresh failed; VPN signal will report false until next attempt.");
            return _cache;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
