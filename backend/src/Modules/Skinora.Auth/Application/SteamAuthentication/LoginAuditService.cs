using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class LoginAuditService : ILoginAuditService
{
    private readonly AppDbContext _db;

    public LoginAuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task RecordLoginAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var entry = new UserLoginLog
        {
            UserId = userId,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : TruncateRequired(ipAddress, 45),
            UserAgent = Truncate(userAgent, 256),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Set<UserLoginLog>().Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value;

    private static string TruncateRequired(string value, int max)
        => value.Length > max ? value[..max] : value;
}
