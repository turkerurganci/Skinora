using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Skinora.Shared.Persistence.Converters;

/// <summary>
/// Ensures all DateTime values read from the database have Kind = Utc.
/// SQL Server returns DateTime with Kind = Unspecified; this converter fixes that.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
