namespace Skinora.Shared.Domain.Seed;

/// <summary>
/// Stable identifiers used by the EF Core seed contract (06 §8.9).
/// </summary>
/// <remarks>
/// These values are baked into migrations and referenced at runtime by
/// platform code (SYSTEM actor for AuditLog / TransactionHistory, singleton
/// heartbeat row lookup). They must remain byte-for-byte stable across
/// releases — changing one effectively orphans every historical audit row.
/// </remarks>
public static class SeedConstants
{
    /// <summary>SYSTEM service account Guid (06 §8.9).</summary>
    public static readonly Guid SystemUserId =
        new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Sentinel SteamId for the SYSTEM account — format-compatible 17-digit
    /// value that is not a real Steam 64-bit identifier (06 §8.9).
    /// </summary>
    public const string SystemSteamId = "00000000000000001";

    /// <summary>SystemHeartbeat is a singleton row pinned to Id = 1 (06 §3.23, §8.9).</summary>
    public const int SystemHeartbeatId = 1;

    /// <summary>
    /// Anchor timestamp for all HasData seed rows. Fixed so migration output
    /// is deterministic across machines; the value itself is not meaningful.
    /// </summary>
    public static readonly DateTime SeedAnchorUtc =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Placeholder <c>RowVersion</c> for HasData rows. SQL Server overwrites
    /// rowversion columns on insert so the value never surfaces in production;
    /// SQLite-backed test hosts require a non-null byte[] because they do not
    /// auto-populate the column.
    /// </summary>
    public static readonly byte[] SeedRowVersion = new byte[8];
}
