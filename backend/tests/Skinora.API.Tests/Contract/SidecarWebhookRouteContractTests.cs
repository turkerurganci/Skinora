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
/// <c>/api/v1/sidecar/steam/trade-offer-events</c>, while
/// <c>SteamWebhooksController</c> serves <c>/api/v1/webhooks/steam/bot-events</c> and
/// <c>/api/v1/webhooks/steam/trade-events</c>. Every publish 404'd, so trade offer
/// state changes never reached the backend and a transaction could never advance to
/// ITEM_ESCROWED. F6's E2E suites could not catch it: they run against
/// <c>sidecar-fake</c>, which used the correct paths — the backend half of the
/// contract was exercised, the real sidecar's half never was.
/// </para>
/// <para>
/// Both halves are read from source rather than hardcoded here, so the test fails on
/// drift from EITHER side: rename the controller route and this breaks; change a
/// sidecar constant and this breaks.
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

    /// <summary>
    /// Backend endpoints retired with the bot custody layer in T117 (02 §2.1):
    /// the platform runs no Steam bots and creates no trade offers, so
    /// <c>SteamWebhooksController</c> is gone. The sidecar-side publishers
    /// (<c>sidecar-steam</c> bot / trade-offer modules and the
    /// <c>sidecar-fake</c> trade route) are deleted in <b>T133</b>, which is
    /// also when this list must go empty again.
    /// </summary>
    /// <remarks>
    /// Listed explicitly rather than skipped so the drift is named, bounded and
    /// visible: every other published path stays strictly guarded, and
    /// <see cref="RetiredPathsAreStillPublished_UntilT133"/> fails the moment
    /// the entry becomes stale — so the exclusion cannot outlive its reason.
    /// </remarks>
    private static readonly string[] RetiredWithBotCustodyLayer =
    [
        "/api/v1/webhooks/steam/bot-events",
        "/api/v1/webhooks/steam/trade-events",
    ];

    [Fact]
    public void EverySidecarPublishedPath_IsServedByBackend()
    {
        var backendRoutes = DiscoverBackendRoutes();
        Assert.NotEmpty(backendRoutes);

        var published = DiscoverSidecarPublishedPaths();
        Assert.NotEmpty(published);

        var orphans = published
            .Where(p => !backendRoutes.Contains(p.Path))
            .Where(p => !RetiredWithBotCustodyLayer.Contains(p.Path, StringComparer.Ordinal))
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
    /// Keeps <see cref="RetiredWithBotCustodyLayer"/> honest from both sides: an
    /// entry may only stay while a sidecar still publishes it (otherwise the
    /// exclusion is dead and hides nothing) and while the backend genuinely no
    /// longer serves it (otherwise the route came back and must be guarded
    /// again). T133 deletes the publishers — this test then fails and the list
    /// is removed with them.
    /// </summary>
    [Fact]
    public void RetiredPathsAreStillPublished_UntilT133()
    {
        var backendRoutes = DiscoverBackendRoutes();
        var published = DiscoverSidecarPublishedPaths()
            .Select(p => p.Path)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var retired in RetiredWithBotCustodyLayer)
        {
            Assert.True(
                published.Contains(retired),
                $"'{retired}' is no longer published by any sidecar — the bot custody "
                + "removal (T133) is done, so drop it from RetiredWithBotCustodyLayer "
                + "and let the strict guard cover it again.");

            Assert.False(
                backendRoutes.Contains(retired),
                $"'{retired}' is served by the backend again — remove it from "
                + "RetiredWithBotCustodyLayer so drift on it is caught.");
        }
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
