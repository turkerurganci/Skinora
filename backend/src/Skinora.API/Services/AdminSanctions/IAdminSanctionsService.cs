using Skinora.Shared.Models;

namespace Skinora.API.Services.AdminSanctions;

/// <summary>
/// Admin write port for the sanctions address list — 07 §9.23–§9.25
/// (AD22/AD23/AD24). Hosts the cross-module orchestration: SanctionedAddress
/// CRUD (Skinora.Platform), AuditLog (Skinora.Platform.Application.Audit) ve
/// retroaktif eşleşme cascade (Skinora.Shared.Sanctions.ISanctionsViolationHandler).
/// </summary>
public interface IAdminSanctionsService
{
    Task<PagedResult<SanctionedAddressDto>> ListAsync(
        AdminSanctionsListQuery query, CancellationToken cancellationToken);

    Task<AddSanctionedAddressOutcome> AddAsync(
        Guid adminId,
        AddSanctionedAddressRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<DeactivateSanctionedAddressOutcome> DeactivateAsync(
        Guid adminId,
        Guid sanctionedAddressId,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public enum AddSanctionedAddressStatus
{
    Added,
    ValidationFailed,
    InvalidAddress,
    AlreadyListed,
}

public sealed record AddSanctionedAddressOutcome(
    AddSanctionedAddressStatus Status,
    SanctionedAddressDto? Body,
    string? ErrorMessage);

public enum DeactivateSanctionedAddressStatus
{
    Deactivated,
    NotFound,
    AlreadyInactive,
}

public sealed record DeactivateSanctionedAddressOutcome(
    DeactivateSanctionedAddressStatus Status,
    DeactivateSanctionedAddressResponse? Body,
    string? ErrorMessage);
