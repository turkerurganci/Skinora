using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Skinora.Shared.Tests.Architecture;

/// <summary>
/// Convention guard: production code must never bypass EF Core (and therefore
/// <c>AppDbContext.EnforceAppendOnly</c>) by issuing raw SQL. Raw SQL skips the
/// <c>ChangeTracker</c>, so an UPDATE/DELETE against an <c>IAppendOnly</c> table
/// would silently evade the immutability guard.
///
/// Pure reflection cannot see method-body call sites, and the owner ruled out a
/// new architecture-test dependency (NetArchTest / ArchUnitNET / Mono.Cecil), so
/// this is a deliberately lightweight source-text scan over <c>backend/src</c>.
/// </summary>
[Trait("Category", "Architecture")]
public class NoRawSqlConventionTests
{
    // The raw-SQL escape hatches EF exposes. None are used in production today;
    // this test fails the build the moment one is introduced.
    private static readonly string[] ForbiddenTokens =
    {
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync",
        "ExecuteSqlInterpolated",
        "ExecuteSqlInterpolatedAsync",
        "FromSqlRaw",
        "FromSqlInterpolated",
    };

    [Fact]
    public void ProductionSource_DoesNotUseRawSql()
    {
        var srcRoot = LocateSourceRoot();

        var csFiles = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsScannable)
            .ToList();

        // Fail loudly on a broken scan root rather than passing vacuously.
        Assert.True(
            csFiles.Count > 0,
            $"No .cs files found under '{srcRoot}' — source-root resolution is broken.");

        var violations = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            var hits = ForbiddenTokens
                .Where(token => text.Contains(token, StringComparison.Ordinal))
                .ToList();
            if (hits.Count > 0)
            {
                violations.Add($"{file}: {string.Join(", ", hits)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Raw SQL (ExecuteSqlRaw / FromSqlRaw / …) bypasses EF's append-only guard and is "
                + "forbidden in production code. Route writes through the DbContext instead. "
                + "Offending files:\n  "
                + string.Join("\n  ", violations));
    }

    private static bool IsScannable(string path)
    {
        if (path.EndsWith(".Designer.cs", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(segment => segment is "obj" or "bin");
    }

    private static string LocateSourceRoot()
    {
        // AppContext.BaseDirectory = backend/tests/Skinora.Shared.Tests/bin/<cfg>/net9.0;
        // walk up to the directory holding Skinora.sln (= backend/), then target src/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Skinora.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate Skinora.sln walking up from the test directory.");

        var srcRoot = Path.Combine(dir!.FullName, "src");
        Assert.True(Directory.Exists(srcRoot), $"Expected source root '{srcRoot}' does not exist.");
        return srcRoot;
    }
}
