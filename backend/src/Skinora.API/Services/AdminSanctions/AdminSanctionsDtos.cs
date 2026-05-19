namespace Skinora.API.Services.AdminSanctions;

/// <summary>AD22/AD23 response satırı (07 §9.23/§9.24).</summary>
/// <param name="Id">SanctionedAddress.Id.</param>
/// <param name="Address">Yaptırımlı cüzdan adresi.</param>
/// <param name="Network">Ağ kimliği (MVP yalnız <c>TRC-20</c>).</param>
/// <param name="Source">Liste kaynağı (<c>MANUAL</c> MVP; <c>OFAC</c>/<c>EU</c>/<c>UN</c> reserved).</param>
/// <param name="Reason">Admin notu (opsiyonel).</param>
/// <param name="ListedAt">Listeye eklenme tarihi.</param>
/// <param name="AddedBy">Adresi ekleyen admin (MANUAL için; auto-sync için <c>null</c>).</param>
/// <param name="IsActive">Aktif/deaktif.</param>
/// <param name="CreatedAt">Satırın oluşturulma tarihi.</param>
/// <param name="UpdatedAt">Son güncelleme (deactivate dahil).</param>
public sealed record SanctionedAddressDto(
    Guid Id,
    string Address,
    string Network,
    string Source,
    string? Reason,
    DateTime ListedAt,
    SanctionedAddressAdminDto? AddedBy,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SanctionedAddressAdminDto(Guid Id, string DisplayName);

/// <summary>AD22 sorgu parametreleri.</summary>
public sealed record AdminSanctionsListQuery(
    string? Network,
    string? Source,
    string? Search,
    bool? IsActive,
    string? SortBy,
    string? SortOrder,
    int Page,
    int PageSize);

/// <summary>AD23 request body.</summary>
public sealed record AddSanctionedAddressRequest(
    string? Address,
    string? Network,
    string? Source,
    string? Reason);

/// <summary>AD24 response body — deactivation ack.</summary>
public sealed record DeactivateSanctionedAddressResponse(
    Guid Id,
    string Address,
    bool IsActive,
    DateTime DeactivatedAt);
