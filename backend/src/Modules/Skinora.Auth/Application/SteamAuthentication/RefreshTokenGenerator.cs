using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.Persistence;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenByteLength = 64;

    private readonly AppDbContext _db;
    private readonly JwtSettings _settings;

    public RefreshTokenGenerator(AppDbContext db, IOptions<JwtSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public async Task<GeneratedRefreshToken> IssueAsync(
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var expiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);
        var plainText = GenerateToken();

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = HashToken(plainText),
            ExpiresAt = expiresAt,
            IsRevoked = false,
            IpAddress = TruncateIpAddress(ipAddress),
            DeviceInfo = TruncateUserAgent(userAgent),
        };

        _db.Set<RefreshToken>().Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new GeneratedRefreshToken(entity, plainText, expiresAt);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string plainText)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));

    private static string? TruncateIpAddress(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length > 45 ? value[..45] : value;

    private static string? TruncateUserAgent(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length > 256 ? value[..256] : value;
}
