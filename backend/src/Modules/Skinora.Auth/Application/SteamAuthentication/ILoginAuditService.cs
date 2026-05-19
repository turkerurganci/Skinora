namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Writes a <c>UserLoginLog</c> row for each successful Steam login —
/// 05 §6.3, 06 §3.2. <paramref name="hasVpnSignal"/> is the T83 supportive
/// signal flag — never blocks the login, persisted for future fraud rules.
/// </summary>
public interface ILoginAuditService
{
    Task RecordLoginAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        bool hasVpnSignal,
        CancellationToken cancellationToken);
}
