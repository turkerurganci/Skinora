using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Skinora.Auth.Configuration;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class AccessTokenGenerator : IAccessTokenGenerator
{
    private readonly JwtSettings _settings;
    private readonly IAdminAuthorityResolver _authorityResolver;
    private readonly JwtSecurityTokenHandler _handler = new();

    public AccessTokenGenerator(
        IOptions<JwtSettings> settings,
        IAdminAuthorityResolver authorityResolver)
    {
        _settings = settings.Value;
        _authorityResolver = authorityResolver;
    }

    public async Task<GeneratedAccessToken> GenerateAsync(
        User user, CancellationToken cancellationToken)
    {
        var authority = await _authorityResolver.ResolveAsync(user.Id, cancellationToken);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes);

        var claims = new List<Claim>
        {
            new(AuthClaimTypes.UserId, user.Id.ToString()),
            new(AuthClaimTypes.SteamId, user.SteamId),
            new(AuthClaimTypes.Role, authority.Role),
        };

        // Permission claims only matter for non-super-admin admins —
        // PermissionAuthorizationHandler short-circuits on role=super_admin
        // and a regular user has no admin permissions to grant.
        foreach (var permission in authority.Permissions)
        {
            claims.Add(new Claim(AuthClaimTypes.Permission, permission));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = credentials,
        };

        var token = _handler.CreateToken(descriptor);
        return new GeneratedAccessToken(_handler.WriteToken(token), expires);
    }
}
