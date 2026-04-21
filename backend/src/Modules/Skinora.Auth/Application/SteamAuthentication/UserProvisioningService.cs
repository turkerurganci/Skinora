using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class UserProvisioningService : IUserProvisioningService
{
    private readonly AppDbContext _db;

    public UserProvisioningService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserProvisioningResult> UpsertFromSteamLoginAsync(
        string steamId64,
        SteamPlayerSummary? profile,
        CancellationToken cancellationToken)
    {
        var existing = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.SteamId == steamId64, cancellationToken);

        if (existing is null)
        {
            var created = new User
            {
                Id = Guid.NewGuid(),
                SteamId = steamId64,
                SteamDisplayName = profile?.PersonaName ?? BuildPlaceholderDisplayName(steamId64),
                SteamAvatarUrl = profile?.AvatarFull,
                PreferredLanguage = "en",
            };

            _db.Set<User>().Add(created);
            await _db.SaveChangesAsync(cancellationToken);
            return new UserProvisioningResult(created, IsNewUser: true);
        }

        if (profile is not null)
        {
            var changed = false;

            if (!string.IsNullOrWhiteSpace(profile.PersonaName) &&
                existing.SteamDisplayName != profile.PersonaName)
            {
                existing.SteamDisplayName = profile.PersonaName;
                changed = true;
            }

            if (profile.AvatarFull is not null &&
                existing.SteamAvatarUrl != profile.AvatarFull)
            {
                existing.SteamAvatarUrl = profile.AvatarFull;
                changed = true;
            }

            if (changed)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        return new UserProvisioningResult(existing, IsNewUser: false);
    }

    private static string BuildPlaceholderDisplayName(string steamId64)
    {
        var suffix = steamId64.Length > 6 ? steamId64[^6..] : steamId64;
        return $"Steam User {suffix}";
    }
}
