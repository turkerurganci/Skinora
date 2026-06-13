using Skinora.Shared.Enums;

namespace Skinora.Steam.Application.Admin;

/// <summary>Body for AD25 — <c>GET /admin/steam-accounts/{botId}/recovery-queue</c> (T103b-2, 04 §8.7).</summary>
public sealed record BotRecoveryQueueResponse(
    Guid BotId,
    PlatformSteamBotStatus BotStatus,
    IReadOnlyList<BotRecoveryQueueItemDto> Items);

/// <summary>
/// One Recovery Queue row (04 §8.7). Triage fields come from
/// <c>BotRecoveryItem</c>; item/party/current-status fields are joined live from
/// the transaction so they never drift from the materialised row.
/// </summary>
public sealed record BotRecoveryQueueItemDto(
    Guid Id,
    Guid TransactionId,
    string ItemName,
    string? ItemIconUrl,
    string SellerSteamId,
    string? SellerDisplayName,
    string? BuyerSteamId,
    string? BuyerDisplayName,
    TransactionStatus CurrentStatus,
    TransactionStatus StatusAtRestriction,
    bool IsOnHold,
    BotRecoveryStatus RecoveryStatus,
    Guid? ResponsibleAdminId,
    string? ResponsibleAdminName,
    string? AdminNote,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

/// <summary>
/// Request for AD26 — <c>PATCH /admin/steam-accounts/recovery/{id}</c>. Each field
/// is optional (PATCH semantics): null = leave unchanged. <c>RecoveryStatus</c>
/// covers "Manual Recovery Başlat" (→ IN_REVIEW) and "Çözüldü" (→ RESOLVED);
/// <c>ResponsibleAdminId</c> covers "Sorumlu Admin Ata/Değiştir"; <c>AdminNote</c>
/// covers "Not Ekle" (empty string clears the note).
/// </summary>
public sealed record UpdateRecoveryItemRequest(
    BotRecoveryStatus? RecoveryStatus,
    Guid? ResponsibleAdminId,
    string? AdminNote);

public enum UpdateRecoveryItemStatus
{
    Updated,
    NotFound,
    ValidationFailed,
    AlreadyResolved,
}

public sealed record UpdateRecoveryItemOutcome(
    UpdateRecoveryItemStatus Status,
    BotRecoveryQueueItemDto? Body,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Error codes surfaced by <see cref="IAdminBotRecoveryService"/> (07 §9 AD26).</summary>
public static class BotRecoveryErrorCodes
{
    public const string RecoveryItemNotFound = "RECOVERY_ITEM_NOT_FOUND";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string AlreadyResolved = "RECOVERY_ALREADY_RESOLVED";
    public const string ResponsibleAdminNotFound = "RESPONSIBLE_ADMIN_NOT_FOUND";
    public const string NoChange = "NO_CHANGE";
}
