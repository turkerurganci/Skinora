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

    // 07 §9.10 failoverStatus vocabulary (T103b-2).
    private const string FailoverStatusNone = "NONE";
    private const string FailoverStatusDiverted = "RESTRICTED_NEW_TXN_DIVERTED";
    private const string FailoverStatusInRecovery = "ACTIVE_TXN_IN_RECOVERY";

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
                b.RestrictionReason,
            })
            .ToListAsync(cancellationToken);

        // Live recovery counts (T103b-2): open (non-RESOLVED) recovery items per
        // bot. Drives RecoveryTransactionCount + the FailoverStatus derivation.
        var recoveryByBot = (await _db.Set<BotRecoveryItem>()
                .AsNoTracking()
                .Where(r => r.RecoveryStatus != BotRecoveryStatus.RESOLVED)
                .GroupBy(r => r.PlatformSteamBotId)
                .Select(g => new { BotId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.BotId, x => x.Count);

        var accounts = rows.Select(b =>
            {
                var recoveryCount = recoveryByBot.GetValueOrDefault(b.Id, 0);
                return new AdminSteamAccountDto(
                    Id: b.Id,
                    Name: b.DisplayName,
                    SteamId: b.SteamId,
                    Status: b.Status,
                    EscrowedItemCount: b.ActiveEscrowCount,
                    DailyTradeOfferCount: b.DailyTradeOfferCount,
                    DailyTradeOfferLimit: SteamDailyTradeOfferLimit,
                    LastHealthCheck: b.LastHealthCheckAt,
                    RestrictionReason: b.RestrictionReason,
                    FailoverStatus: DeriveFailoverStatus(b.Status, recoveryCount),
                    RecoveryTransactionCount: recoveryCount);
            })
            .ToList();

        var warning = BuildWarning(accounts);

        return new AdminSteamAccountsResponse(
            Accounts: accounts,
            WarningMessage: warning);
    }

    /// <summary>
    /// 07 §9.10 — ACTIVE bots report NONE; a degraded bot with no open recovery
    /// items has only had its new traffic diverted (RESTRICTED_NEW_TXN_DIVERTED),
    /// while one still holding stuck escrows is ACTIVE_TXN_IN_RECOVERY.
    /// </summary>
    private static string DeriveFailoverStatus(PlatformSteamBotStatus status, int openRecoveryCount)
    {
        if (status == PlatformSteamBotStatus.ACTIVE)
        {
            return FailoverStatusNone;
        }
        return openRecoveryCount > 0 ? FailoverStatusInRecovery : FailoverStatusDiverted;
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
