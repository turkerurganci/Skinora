using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Sanctions;
using Skinora.Users.Domain.Entities;

namespace Skinora.Fraud.Application.Sanctions;

/// <inheritdoc cref="ISanctionsViolationHandler"/>
public sealed class SanctionsViolationHandler : ISanctionsViolationHandler
{
    private readonly IFraudFlagService _flags;
    private readonly AppDbContext _db;

    public SanctionsViolationHandler(IFraudFlagService flags, AppDbContext db)
    {
        _flags = flags;
        _db = db;
    }

    public Task RecordWalletAttemptAsync(
        Guid userId,
        string attemptedAddress,
        string matchedList,
        CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            source = "wallet_attempt",
            attemptedAddress,
            matchedList,
        });
        var reason = $"Sanctions match on wallet attempt ({matchedList})";
        return StageIfMissingAsync(userId, details, reason, cancellationToken);
    }

    public async Task RecordLoginAttemptAsync(
        string steamId64,
        string matchedList,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
            return;

        var userId = await _db.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => u.SteamId == steamId64)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
            return;

        var details = JsonSerializer.Serialize(new
        {
            source = "login",
            steamId64,
            matchedList,
        });
        var reason = $"Sanctions match on login ({matchedList})";
        await StageIfMissingAsync(userId, details, reason, cancellationToken);
    }

    public Task RecordRetroactiveMatchAsync(
        Guid userId,
        string matchedAddress,
        string matchedList,
        CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            source = "admin_retroactive",
            matchedAddress,
            matchedList,
        });
        var reason = $"Sanctions match on admin retroactive scan ({matchedList})";
        return StageIfMissingAsync(userId, details, reason, cancellationToken);
    }

    private async Task StageIfMissingAsync(
        Guid userId,
        string details,
        string emergencyHoldReason,
        CancellationToken cancellationToken)
    {
        // Idempotency: if the user already has a PENDING account-level
        // SANCTIONS_MATCH flag, skip — admin still has the original open
        // for review. The emergency-hold cascade in FraudFlagService is
        // itself idempotent (skips transactions already on hold), but
        // we avoid creating duplicate FraudFlag rows.
        var hasOpenFlag = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .AnyAsync(
                f => f.UserId == userId
                     && f.Type == FraudFlagType.SANCTIONS_MATCH
                     && f.Scope == FraudFlagScope.ACCOUNT_LEVEL
                     && f.Status == ReviewStatus.PENDING
                     && !f.IsDeleted,
                cancellationToken);

        if (hasOpenFlag)
            return;

        await _flags.StageAccountFlagAsync(
            userId: userId,
            type: FraudFlagType.SANCTIONS_MATCH,
            details: details,
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cascadeEmergencyHold: true,
            emergencyHoldReason: emergencyHoldReason,
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
