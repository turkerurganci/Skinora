namespace Skinora.Steam.Application.Admin;

/// <summary>
/// Read port for the admin steam-accounts panel (T63 — 07 §9.10).
/// </summary>
public interface IAdminSteamBotQueryService
{
    /// <summary>
    /// AD10 — list every platform Steam bot with its operational status.
    /// Returns a stable ordering (DisplayName ascending) so admins see the
    /// same row layout across reloads.
    /// </summary>
    Task<AdminSteamAccountsResponse> ListAsync(CancellationToken cancellationToken);
}
