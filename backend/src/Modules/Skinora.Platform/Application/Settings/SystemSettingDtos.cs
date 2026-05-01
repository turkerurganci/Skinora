namespace Skinora.Platform.Application.Settings;

/// <summary>Body of 07 §9.8 — <c>GET /admin/settings</c> response.</summary>
public sealed record SettingsListResponse(IReadOnlyList<SettingItemDto> Settings);

/// <summary>One row in <see cref="SettingsListResponse.Settings"/> (07 §9.8).</summary>
public sealed record SettingItemDto(
    string Key,
    string? Value,
    string Category,
    string Label,
    string? Description,
    string? Unit,
    string ValueType);

/// <summary>Body of 07 §9.9 — <c>PUT /admin/settings/:key</c> request.</summary>
public sealed record UpdateSettingRequest(string? Value);

/// <summary>Body of 07 §9.9 success response.</summary>
public sealed record UpdateSettingResponse(
    string Key,
    string? Value,
    DateTime UpdatedAt);
