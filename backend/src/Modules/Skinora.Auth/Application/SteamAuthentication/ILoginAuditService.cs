namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Writes a <c>UserLoginLog</c> row for each successful Steam login —
/// 05 §6.3, 06 §3.2.
/// </summary>
public interface ILoginAuditService
{
    Task RecordLoginAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken);
}
