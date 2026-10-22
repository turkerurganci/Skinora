using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T169 — the payout/refund launch gate (11 T175 "otomatik ödeme kapıları
/// kapalı başlıyor"; proje sahibi kararı 2026-10-21: test boyunca bütün
/// ödemeler admin onayı gerektirir). ⚠ PARA.
/// </summary>
/// <remarks>
/// <para>
/// While <see cref="AutoSendKey"/> is <c>false</c> the outgoing-transfer
/// dispatcher sends a money-out row — <see cref="RequiresApproval"/>: the
/// seller payout and every buyer refund — only after an admin approved it
/// (<c>BlockchainTransaction.ApprovedAt</c>, AD41). The sweep (deposit → hot
/// wallet) is the platform moving its own money between its own addresses, so
/// it never waits. The gate does not change what a row pays or to whom — the
/// amount and the recipient are fixed when the row is created — it only holds
/// the moment it is sent.
/// </para>
/// <para>
/// <b>Fail closed.</b> A missing, unconfigured or unparsable setting reads as
/// "approval required": an unreadable gate must never let money out on its
/// own. The row is seeded <c>false</c> and is not env-bootstrappable (the
/// T125 / T129 rule) — only an admin opens it, from the settings screen, and
/// that write is audited like every other setting change.
/// </para>
/// </remarks>
public static class OutboundApprovalGate
{
    /// <summary>SystemSetting key: <c>true</c> = send without approval.</summary>
    public const string AutoSendKey = "outbound.auto_send_enabled";

    /// <summary>The outbound types the gate holds (all but SWEEP).</summary>
    public static readonly BlockchainTransactionType[] ApprovalTypes =
    [
        BlockchainTransactionType.SELLER_PAYOUT,
        BlockchainTransactionType.BUYER_REFUND,
        BlockchainTransactionType.EXCESS_REFUND,
        BlockchainTransactionType.WRONG_TOKEN_REFUND,
        BlockchainTransactionType.INCORRECT_AMOUNT_REFUND,
        BlockchainTransactionType.LATE_PAYMENT_REFUND,
    ];

    /// <summary>True when a row of <paramref name="type"/> waits for an approval while the gate is closed.</summary>
    public static bool RequiresApproval(BlockchainTransactionType type) =>
        Array.IndexOf(ApprovalTypes, type) >= 0;

    /// <summary>
    /// True only when the setting is configured and reads <c>true</c>;
    /// anything else (missing, unconfigured, unparsable) is <c>false</c>.
    /// </summary>
    public static async Task<bool> IsAutoSendEnabledAsync(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var raw = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == AutoSendKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return bool.TryParse(raw, out var enabled) && enabled;
    }
}
