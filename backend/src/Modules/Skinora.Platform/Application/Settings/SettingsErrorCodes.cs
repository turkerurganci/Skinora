namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Stable error codes for 07 §9.8–§9.9. Mirrors the convention used by
/// <c>AdminRoleErrorCodes</c> / <c>NotificationInboxErrorCodes</c> so the
/// <see cref="Skinora.Shared.Models.ApiResponse{T}"/> envelope carries
/// human-readable codes rather than ad-hoc strings.
/// </summary>
public static class SettingsErrorCodes
{
    /// <summary>404 — admin requested a key not in the catalog/seed (07 §9.9).</summary>
    public const string SettingNotFound = "SETTING_NOT_FOUND";

    /// <summary>400 — value failed type/range/cross-key validation (07 §9.9).</summary>
    public const string ValidationError = "VALIDATION_ERROR";
}
