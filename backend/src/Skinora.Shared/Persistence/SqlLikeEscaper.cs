namespace Skinora.Shared.Persistence;

/// <summary>
/// Escapes user input before it is interpolated into a SQL <c>LIKE</c> pattern,
/// so wildcard characters (<c>%</c>, <c>_</c>, <c>[</c>) are matched literally
/// instead of acting as wildcards (LIKE-wildcard injection — distinct from SQL
/// injection, which EF parameterization already prevents).
/// </summary>
/// <remarks>
/// Uses SQL Server bracket-wrapping, which works without an <c>ESCAPE</c> clause
/// (the standard 2-arg <see cref="Microsoft.EntityFrameworkCore.DbFunctionsExtensions.Like(Microsoft.EntityFrameworkCore.DbFunctions, string, string)"/>
/// overload does not emit one). Order matters: <c>[</c> must be rewritten first,
/// otherwise the brackets introduced by the later rewrites would be re-processed.
/// Callers still wrap the result in their own <c>%...%</c> wildcards.
/// </remarks>
public static class SqlLikeEscaper
{
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
    }
}
