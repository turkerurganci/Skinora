using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Tests.Unit;

public class AccessTokenGeneratorTests
{
    private const string Secret = "unit-test-jwt-secret-key-minimum-32-characters!";
    private const string Issuer = "skinora-test";
    private const string Audience = "skinora-test-client";

    private static AccessTokenGenerator CreateGenerator() =>
        new(Options.Create(new JwtSettings
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 7,
        }));

    [Fact]
    public void Generate_ReturnsTokenWithExpectedClaims()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Unit",
        };

        var result = CreateGenerator().Generate(user);

        Assert.NotEmpty(result.Token);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.Token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateLifetime = true,
        }, out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(AuthClaimTypes.UserId)?.Value);
        Assert.Equal(user.SteamId, principal.FindFirst(AuthClaimTypes.SteamId)?.Value);
        Assert.Equal(AuthRoles.User, principal.FindFirst(AuthClaimTypes.Role)?.Value);
    }
}
