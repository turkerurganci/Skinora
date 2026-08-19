using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Skinora.API.Tests.Contract;

/// <summary>
/// Pins the sidecar → backend webhook contract: every <c>/api/v1/...</c> endpoint a
/// sidecar publishes to must be a route this backend actually serves.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: the real Steam sidecar published to
/// <c>/api/v1/sidecar/steam/bot-events</c> and
/// <c>/api/v1/sidecar/steam/trade-offer-events</c>, while the then-existing
/// <c>SteamWebhooksController</c> served <c>/api/v1/webhooks/steam/bot-events</c> and
/// <c>/api/v1/webhooks/steam/trade-events</c>. Every publish 404'd, so trade offer
/// state changes never reached the backend and a transaction could never advance past
/// the escrow step of the then-current custodial flow (a state set the v3.0 P2P pivot
/// has since retired — 05 §4.1). F6's E2E suites could not catch it: they run against
/// <c>sidecar-fake</c>, which used the correct paths — the backend half of the
/// contract was exercised, the real sidecar's half never was.
/// </para>
/// <para>
/// Both halves are read from source rather than hardcoded here, so the test fails on
/// drift from EITHER side: rename the controller route and this breaks; change a
/// sidecar constant and this breaks.
/// </para>
/// <para>
/// The guard is strict again as of T133. Between T117 and T132 it carried a named
/// exception for those two <c>/webhooks/steam/*</c> paths: the controller was gone
/// but <c>sidecar-steam</c>'s bot / trade-offer modules still published to them.
/// T133 deleted the publishers, so the exception had nothing left to cover and went
/// with them — every published path is checked again without exemption.
/// </para>
/// </remarks>
public sealed class SidecarWebhookRouteContractTests
{
    /// <summary>
    /// Sidecar source directories scanned for published endpoint literals, relative to
    /// the repository root. <c>sidecar-fake</c> is included because the E2E stack's
    /// correctness depends on the same contract.
    /// </summary>
    private static readonly string[] SidecarSourceDirs =
    [
        Path.Combine("sidecar-steam", "src"),
        Path.Combine("sidecar-blockchain", "src"),
        Path.Combine("sidecar-fake", "src"),
    ];

    /// <summary>
    /// Matches an <c>/api/v1/...</c> path inside a single- or double-quoted TS literal.
    /// Template literals (<c>`...${x}`</c>) are intentionally not matched — a computed
    /// path cannot be compared statically.
    /// </summary>
    private static readonly Regex ApiPathLiteral = new(
        @"['""](?<path>/api/v1/[A-Za-z0-9\-_/]+)['""]",
        RegexOptions.Compiled);

    [Fact]
    public void EverySidecarPublishedPath_IsServedByBackend()
    {
        var backendRoutes = DiscoverBackendRoutes();
        Assert.NotEmpty(backendRoutes);

        var published = DiscoverSidecarPublishedPaths();
        Assert.NotEmpty(published);

        var orphans = published
            .Where(p => !backendRoutes.Contains(p.Path))
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .ToList();

        if (orphans.Count > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                orphans.Select(o => $"  - {o.Path}   (declared in {o.SourceFile})"));
            var known = string.Join(
                Environment.NewLine,
                backendRoutes.OrderBy(r => r, StringComparer.Ordinal).Select(r => $"  - {r}"));

            Assert.Fail(
                "Sidecar publishes to path(s) this backend does not serve — every call would 404:"
                + Environment.NewLine + detail
                + Environment.NewLine + Environment.NewLine
                + "Backend routes available:" + Environment.NewLine + known);
        }
    }

    /// <summary>
    /// Guards the two routes whose absence silently breaks the escrow flow, with a
    /// failure message that names them explicitly rather than relying on set diffing.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/webhooks/blockchain/payment-detected")]
    [InlineData("/api/v1/webhooks/blockchain/payment-confirmed")]
    public void CriticalSidecarRoute_IsServedByBackend(string route)
    {
        Assert.Contains(route, DiscoverBackendRoutes());
    }

    /// <summary>
    /// The custody layer left no published path behind: nothing under
    /// <c>/api/v1/webhooks/steam/</c> is emitted by any sidecar any more (T133
    /// deleted the last three constants, in <c>sidecar-steam</c>'s
    /// <c>BotManager</c>, <c>TradeOfferService</c> and <c>TradeOfferMonitor</c>).
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed: <see cref="EverySidecarPublishedPath_IsServedByBackend"/>
    /// would stay green if such a path came back AND a matching backend route came
    /// back with it — which is exactly how the custody surface would be resurrected
    /// by accident. This test fails on the publisher alone.
    /// </remarks>
    [Fact]
    public void NoSidecarPublishesToTheRetiredSteamWebhookSurface()
    {
        var resurrected = DiscoverSidecarPublishedPaths()
            .Where(p => p.Path.StartsWith("/api/v1/webhooks/steam", StringComparison.Ordinal))
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            resurrected.Count == 0,
            "The Steam webhook surface was retired with the bot custody layer (02 §2.1) "
            + "and the backend serves none of it, but a sidecar publishes:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                resurrected.Select(o => $"  - {o.Path}   (declared in {o.SourceFile})")));
    }

    // ------------------------------------------------------------------
    // Backend side — reflect over controller routing attributes.
    // ------------------------------------------------------------------
    private static HashSet<string> DiscoverBackendRoutes()
    {
        var assembly = typeof(Program).Assembly;
        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var controller in assembly.GetTypes()
                     .Where(t => t is { IsAbstract: false, IsClass: true }
                                 && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            var prefix = controller
                .GetCustomAttributes<RouteAttribute>()
                .Select(a => a.Template)
                .FirstOrDefault();
            if (prefix is null) continue;

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                         | BindingFlags.DeclaredOnly))
            {
                foreach (var http in action.GetCustomAttributes()
                             .OfType<IActionHttpMethodProvider>()
                             .OfType<IRouteTemplateProvider>())
                {
                    routes.Add(Combine(prefix, http.Template));
                }
            }
        }

        return routes;
    }

    private static string Combine(string prefix, string? template)
    {
        var head = "/" + prefix.Trim('/');
        return string.IsNullOrWhiteSpace(template) ? head : head + "/" + template.Trim('/');
    }

    // ------------------------------------------------------------------
    // Sidecar side — scan TypeScript sources for published path literals.
    // ------------------------------------------------------------------
    private static List<(string Path, string SourceFile)> DiscoverSidecarPublishedPaths()
    {
        var root = FindRepositoryRoot();
        var results = new List<(string, string)>();

        foreach (var relativeDir in SidecarSourceDirs)
        {
            var dir = Path.Combine(root, relativeDir);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.ts", SearchOption.AllDirectories))
            {
                // Test files may reference historical or fabricated paths on purpose.
                if (file.EndsWith(".test.ts", StringComparison.Ordinal)) continue;

                foreach (Match match in ApiPathLiteral.Matches(File.ReadAllText(file)))
                {
                    results.Add((
                        match.Groups["path"].Value,
                        Path.GetRelativePath(root, file).Replace('\\', '/')));
                }
            }
        }

        return results;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found walking up from '{AppContext.BaseDirectory}' "
            + "(looked for docker-compose.yml).");
    }
}
