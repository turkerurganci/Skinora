namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Platform uptime singleton. All fields per 06 §3.23.
/// The row count is pinned to one by a CHECK (Id = 1) constraint so
/// outage-window calculations always read from a single, well-known row
/// (05 §4.4).
/// </summary>
public class SystemHeartbeat
{
    public int Id { get; set; } = 1;
    public DateTime LastHeartbeat { get; set; }
    public DateTime UpdatedAt { get; set; }
}
