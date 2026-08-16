using Microsoft.EntityFrameworkCore;

namespace Skinora.Transactions.Infrastructure.Persistence;

/// <summary>
/// Recognises the database's own answer to a uniqueness rule inside a
/// <see cref="DbUpdateException"/>.
/// </summary>
/// <remarks>
/// Extracted (T128) from <c>PaymentAddressAllocator</c>, which has carried this
/// predicate since T70 and now shares it with the 02 §2.3 one-open-transaction
/// -per-item gate. Both sites exist for one reason: a unique index is the only
/// writer that sees concurrent inserts, so whichever caller loses that race is
/// told about the rule by SQL Server — not by its own, by-then-stale, read.
/// Behaviour is unchanged from the T70 original.
/// </remarks>
internal static class DbConstraintViolations
{
    /// <summary>
    /// Whether the failed save collided with a unique index or primary key.
    /// </summary>
    public static bool IsUnique(DbUpdateException ex)
    {
        // SQL Server: 2627 (PK violation), 2601 (UNIQUE index violation).
        // SQLite (used by some integration test scenarios): SqliteErrorCode 19
        // surfaces as InnerException.Message containing "UNIQUE constraint".
        var inner = ex?.InnerException;
        if (inner is null) return false;

        var sqlNumber = TryGetSqlNumber(inner);
        if (sqlNumber is 2627 or 2601) return true;

        return inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && inner.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryGetSqlNumber(Exception ex)
    {
        // Avoid hard-referencing Microsoft.Data.SqlClient from the module
        // assembly — reflect once and accept the cost. The shared persistence
        // layer already references SqlClient transitively.
        var prop = ex.GetType().GetProperty("Number");
        if (prop is null || prop.PropertyType != typeof(int)) return null;
        return (int?)prop.GetValue(ex);
    }
}
