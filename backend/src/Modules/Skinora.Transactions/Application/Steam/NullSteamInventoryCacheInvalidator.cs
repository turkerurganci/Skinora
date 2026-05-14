namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// No-op default for <see cref="ISteamInventoryCacheInvalidator"/>. The
/// sidecar-backed implementation in <c>Skinora.Steam</c> overrides the
/// production registration; tests rely on this fallback so they do not need
/// to spin up an HTTP client to assert business behavior.
/// </summary>
public sealed class NullSteamInventoryCacheInvalidator : ISteamInventoryCacheInvalidator
{
    public Task InvalidateAsync(string steamId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
