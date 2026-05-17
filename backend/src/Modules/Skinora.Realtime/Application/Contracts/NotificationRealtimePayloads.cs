namespace Skinora.Realtime.Application.Contracts;

/// <summary>
/// Server→client payloads pushed on <c>/hubs/notifications</c> per 07 §11.2.
/// All payloads are camel-cased on the wire by the SignalR JSON protocol
/// (configured in <c>Program.cs</c> with <c>JsonStringEnumConverter</c>).
/// </summary>
public static class NotificationRealtimePayloads
{
    /// <summary>
    /// Pushed when a fresh <see cref="Skinora.Notifications.Domain.Entities.Notification"/>
    /// row lands in the user's inbox. Field set matches the
    /// <see cref="Skinora.Notifications.Application.Inbox.NotificationListItemDto"/>
    /// minus the <c>isRead</c> flag (a brand-new row is always unread).
    /// </summary>
    public sealed record NewNotification(
        Guid Id,
        string Type,
        string Message,
        string? TargetType,
        Guid? TargetId,
        DateTime CreatedAt);

    /// <summary>
    /// Pushed every time the user's unread-notification count changes
    /// (new notification, mark-read, mark-all-read).
    /// </summary>
    public sealed record UnreadCountChanged(int UnreadCount);

    /// <summary>
    /// Pushed when the user finishes the Telegram bot link flow (T79
    /// forward-deferred — webhook fires this from the <c>/start</c> handler).
    /// </summary>
    public sealed record TelegramConnected(string Username);

    /// <summary>
    /// Pushed when the user finishes the Discord OAuth flow (T80
    /// forward-deferred — callback fires this after token exchange).
    /// </summary>
    public sealed record DiscordConnected(string Username);

    /// <summary>
    /// Pushed when the platform maintenance status changes. Frontend renders
    /// the C08 banner (04 §7.7) and freezes timeouts when <c>active</c>
    /// transitions to <c>true</c>. T-future maintenance toggle endpoint will
    /// fire this; T62 wires the publisher and payload only.
    /// </summary>
    public sealed record MaintenanceStatusChanged(
        bool Active,
        string? Type,
        string? Message,
        DateTime? PlannedEnd);

    /// <summary>
    /// Pushed when a platform Steam bot transitions to RESTRICTED / BANNED /
    /// OFFLINE or is removed from the pool (T69 — 02 §15, 05 §3.2). Frontend
    /// admin dashboard (S18 — bound in T103) renders the alert; non-admin
    /// clients ignore the event but receive the broadcast.
    /// </summary>
    public sealed record AdminBotStatusChanged(
        Guid BotId,
        string SteamId,
        string DisplayName,
        string PreviousStatus,
        string NewStatus,
        string Reason,
        DateTime ChangedAt);

    /// <summary>
    /// Pushed when the daily reconciliation job (T76 — 05 §3.3) detects an
    /// on-chain vs ledger mismatch. One push per (scope, token) finding.
    /// <c>Scope</c> ∈ {<c>DepositAddress</c>, <c>HotWallet</c>, <c>ColdWallet</c>};
    /// <c>Address</c> is the on-chain address that failed reconciliation;
    /// <c>Delta</c> is <c>Actual − Expected</c> (positive = surplus on chain,
    /// negative = missing). Admin dashboard surfaces this alongside the
    /// matching AuditLog row (RECONCILIATION_MISMATCH). Non-admin clients
    /// receive the broadcast but ignore it (no per-role group yet).
    /// </summary>
    public sealed record AdminReconciliationMismatch(
        string Scope,
        string Address,
        string Token,
        decimal Expected,
        decimal Actual,
        decimal Delta,
        long? BlockNumber,
        DateTime DetectedAt);

    /// <summary>
    /// Pushed when the hot wallet monitor job (T77 — 05 §3.3) detects a
    /// balance crossing an admin-configured threshold. <c>Direction</c> ∈
    /// {<c>Upper</c>, <c>Lower</c>}: <c>Upper</c> = stablecoin balance
    /// (USDT/USDC) exceeded <c>hot_wallet_limit</c> — admin should sweep to
    /// cold wallet; <c>Lower</c> = TRX balance dropped below
    /// <c>hot_wallet.trx_balance_minimum</c> — admin should top up TRX
    /// reserves for gas. Best-effort like <c>AdminReconciliationMismatch</c>;
    /// the <c>HOT_WALLET_THRESHOLD_BREACHED</c> AuditLog row is the durable
    /// record.
    /// </summary>
    public sealed record AdminHotWalletThresholdBreached(
        string Token,
        string Direction,
        decimal Threshold,
        decimal Actual,
        long? BlockNumber,
        DateTime DetectedAt);
}
