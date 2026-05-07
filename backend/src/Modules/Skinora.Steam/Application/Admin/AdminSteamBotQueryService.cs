using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;

namespace Skinora.Steam.Application.Admin;

/// <inheritdoc cref="IAdminSteamBotQueryService"/>
public sealed class AdminSteamBotQueryService : IAdminSteamBotQueryService
{
    /// <summary>
    /// Steam-enforced ToS limit: 200 outgoing trade offers per bot per 24h.
    /// Not a SystemSetting — Valve sets it on the protocol side, so the
    /// platform exposes the constant verbatim (07 §9.10 example reflects this).
    /// </summary>
    public const int SteamDailyTradeOfferLimit = 200;

    /// <summary>Forward-deferred to T69 (Steam Sidecar failover).</summary>
    private const string FailoverStatusNone = "NONE";

    private readonly AppDbContext _db;

    public AdminSteamBotQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminSteamAccountsResponse> ListAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Set<PlatformSteamBot>()
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.DisplayName)
            .ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.DisplayName,
                b.SteamId,
                b.Status,
                b.ActiveEscrowCount,
                b.DailyTradeOfferCount,
                b.LastHealthCheckAt,
            })
            .ToListAsync(cancellationToken);

        var accounts = rows.Select(b => new AdminSteamAccountDto(
                Id: b.Id,
                Name: b.DisplayName,
                SteamId: b.SteamId,
                Status: b.Status,
                EscrowedItemCount: b.ActiveEscrowCount,
                DailyTradeOfferCount: b.DailyTradeOfferCount,
                DailyTradeOfferLimit: SteamDailyTradeOfferLimit,
                LastHealthCheck: b.LastHealthCheckAt,
                RestrictionReason: null,
                FailoverStatus: FailoverStatusNone,
                RecoveryTransactionCount: 0))
            .ToList();

        var warning = BuildWarning(accounts);

        return new AdminSteamAccountsResponse(
            Accounts: accounts,
            WarningMessage: warning);
    }

    /// <summary>
    /// Per 07 §9.10: <c>warningMessage</c> is non-null when at least one bot
    /// is not <c>ACTIVE</c>. The text is a Turkish summary the dashboard can
    /// render verbatim — admins prefer a single banner over a per-row badge.
    /// </summary>
    private static string? BuildWarning(IReadOnlyList<AdminSteamAccountDto> accounts)
    {
        var degraded = accounts
            .Where(a => a.Status != PlatformSteamBotStatus.ACTIVE)
            .ToList();
        if (degraded.Count == 0) return null;

        var byStatus = degraded.GroupBy(a => a.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Count()}");
        return $"Sorunlu bot hesabı tespit edildi — {string.Join(", ", byStatus)}.";
    }
}
