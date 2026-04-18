namespace Skinora.Shared.Domain;

/// <summary>
/// Marker interface for entities that are append-only: INSERT is allowed,
/// UPDATE and DELETE are rejected at the <c>AppDbContext</c> level
/// (06 §3.20, §3.22, §4.2).
/// </summary>
public interface IAppendOnly
{
}
