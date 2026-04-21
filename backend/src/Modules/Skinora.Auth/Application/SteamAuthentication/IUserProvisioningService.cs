using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Upserts the <see cref="User"/> record for a Steam login — 03 §2.1.
/// Returns the resolved user plus a flag indicating whether the row was
/// created in this request (used to choose
/// <c>status=new_user</c> vs <c>status=success</c> in 07 §4.3).
/// </summary>
public interface IUserProvisioningService
{
    Task<UserProvisioningResult> UpsertFromSteamLoginAsync(
        string steamId64,
        SteamPlayerSummary? profile,
        CancellationToken cancellationToken);
}

public sealed record UserProvisioningResult(User User, bool IsNewUser);
