using System.Net;

namespace Skinora.API.Configuration;

/// <summary>
/// T166 — keeps the request's direct TCP peer address after
/// <c>UseForwardedHeaders</c> has rewritten <c>Connection.RemoteIpAddress</c>
/// to the client's. Registered as the first middleware in <c>Program.cs</c>.
/// </summary>
public static class DirectPeer
{
    private static readonly object ItemKey = new();

    public static Task CaptureAsync(HttpContext context, Func<Task> next)
    {
        context.Items[ItemKey] = context.Connection.RemoteIpAddress;
        return next();
    }

    /// <summary>
    /// The captured peer, or <c>null</c> when nothing was captured (the
    /// middleware did not run, or the transport has no remote address).
    /// Callers must treat <c>null</c> as untrusted.
    /// </summary>
    public static IPAddress? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as IPAddress : null;

    /// <summary>Test seam: what <see cref="CaptureAsync"/> would have stored.</summary>
    public static void Set(HttpContext context, IPAddress? peer) => context.Items[ItemKey] = peer;
}
