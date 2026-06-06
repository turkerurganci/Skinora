using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services.UserSuspension;

/// <inheritdoc cref="IAdminUserSuspensionService"/>
/// <remarks>
/// Each method commits the User state change + audit row + outbox notification
/// event inside a single <see cref="DbContext.SaveChangesAsync"/> (09 §13.3).
/// Audit reuses <see cref="AuditAction.USER_BANNED"/> / <see cref="AuditAction.USER_UNBANNED"/>
/// (already mapped to the ADMIN_ACTION category). Suspension does NOT block
/// login (unlike <c>IsDeactivated</c>) — enforcement is at the fund-flow
/// mutation services + the <c>/auth/me</c> <c>isSuspended</c> flag.
/// </remarks>
public sealed class AdminUserSuspensionService : IAdminUserSuspensionService
{
    /// <summary>Minimum trimmed length of the suspension reason (mirrors AD19b).</summary>
    public const int MinReasonLength = 10;

    /// <summary>
    /// Maximum suspension reason length — matches the <c>nvarchar(500)</c> column
    /// (UserConfiguration) and the sibling <c>AdminSanctionsService</c> guard, so an
    /// over-long reason returns a clean 400 instead of a SaveChanges truncation 500.
    /// </summary>
    public const int MaxReasonLength = 500;

    /// <summary>
    /// Upper bound for a temporary suspension (≈27 years). Caps <c>durationDays</c>
    /// so an absurd value returns a clean 400 rather than overflowing
    /// <see cref="DateTime"/> inside <c>AddDays</c> (which would surface as a 500).
    /// </summary>
    public const int MaxDurationDays = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly AppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;

    public AdminUserSuspensionService(
        AppDbContext db,
        IAuditLogger audit,
        IOutboxService outbox,
        TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<SuspendUserOutcome> SuspendAsync(
        Guid adminUserId,
        Guid targetUserId,
        SuspendUserRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: validation ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return SuspendFailure(SuspendUserStatus.ValidationFailed,
                UserSuspensionErrorCodes.ValidationError,
                $"reason must be at least {MinReasonLength} characters.");

        if (trimmedReason.Length > MaxReasonLength)
            return SuspendFailure(SuspendUserStatus.ValidationFailed,
                UserSuspensionErrorCodes.ValidationError,
                $"reason must not exceed {MaxReasonLength} characters.");

        if (request.DurationDays is <= 0)
            return SuspendFailure(SuspendUserStatus.ValidationFailed,
                UserSuspensionErrorCodes.ValidationError,
                "durationDays must be a positive number, or null for a permanent suspension.");

        if (request.DurationDays > MaxDurationDays)
            return SuspendFailure(SuspendUserStatus.ValidationFailed,
                UserSuspensionErrorCodes.ValidationError,
                $"durationDays must not exceed {MaxDurationDays}.");

        // ---------- Stage 2: load + guard ----------
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == targetUserId && !u.IsDeleted, cancellationToken);
        if (user is null)
            return SuspendFailure(SuspendUserStatus.NotFound,
                UserSuspensionErrorCodes.UserNotFound, "User not found.");

        if (user.IsSuspended)
            return SuspendFailure(SuspendUserStatus.AlreadySuspended,
                UserSuspensionErrorCodes.AlreadySuspended, "User is already suspended.");

        // ---------- Stage 3: stamp suspension ----------
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = request.DurationDays.HasValue
            ? nowUtc.AddDays(request.DurationDays.Value)
            : (DateTime?)null;

        user.IsSuspended = true;
        user.SuspendedAt = nowUtc;
        user.SuspensionReason = trimmedReason;
        user.SuspensionExpiresAt = expiresAt;

        // ---------- Stage 4: side effects ----------
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: targetUserId,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.USER_BANNED,
                EntityType: nameof(User),
                EntityId: targetUserId.ToString(),
                OldValue: null,
                NewValue: JsonSerializer.Serialize(new
                {
                    Reason = trimmedReason,
                    ExpiresAt = expiresAt,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _outbox.PublishAsync(
            new AccountSuspendedEvent(
                EventId: Guid.NewGuid(),
                UserId: targetUserId,
                Reason: trimmedReason,
                ExpiresAt: expiresAt,
                OccurredAt: nowUtc),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new SuspendUserOutcome(
            Status: SuspendUserStatus.Suspended,
            Body: new SuspendUserResponse(
                UserId: targetUserId,
                SuspendedAt: nowUtc,
                Reason: trimmedReason,
                ExpiresAt: expiresAt),
            ErrorCode: null,
            ErrorMessage: null);
    }

    public async Task<UnsuspendUserOutcome> UnsuspendAsync(
        Guid actorUserId,
        Guid targetUserId,
        ActorType actorType,
        bool automatic,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == targetUserId && !u.IsDeleted, cancellationToken);
        if (user is null)
            return new UnsuspendUserOutcome(UnsuspendUserStatus.NotFound, null,
                UserSuspensionErrorCodes.UserNotFound, "User not found.");

        if (!user.IsSuspended)
            return new UnsuspendUserOutcome(UnsuspendUserStatus.NotSuspended, null,
                UserSuspensionErrorCodes.NotSuspended, "User is not suspended.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        user.IsSuspended = false;
        user.SuspendedAt = null;
        user.SuspensionReason = null;
        user.SuspensionExpiresAt = null;

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: targetUserId,
                ActorId: actorUserId,
                ActorType: actorType,
                Action: AuditAction.USER_UNBANNED,
                EntityType: nameof(User),
                EntityId: targetUserId.ToString(),
                OldValue: null,
                NewValue: JsonSerializer.Serialize(new { Automatic = automatic }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _outbox.PublishAsync(
            new AccountUnsuspendedEvent(
                EventId: Guid.NewGuid(),
                UserId: targetUserId,
                Automatic: automatic,
                OccurredAt: nowUtc),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new UnsuspendUserOutcome(
            Status: UnsuspendUserStatus.Unsuspended,
            Body: new UnsuspendUserResponse(targetUserId, nowUtc),
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static SuspendUserOutcome SuspendFailure(
        SuspendUserStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
