using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Tests.Unit;

public class AccessTokenGeneratorTests
{
    private const string Secret = "unit-test-jwt-secret-key-minimum-32-characters!";
    private const string Issuer = "skinora-test";
    private const string Audience = "skinora-test-client";

    private static AccessTokenGenerator CreateGenerator(IAdminAuthorityResolver resolver) =>
        new(Options.Create(new JwtSettings
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 7,
        }), resolver);

    [Fact]
    public async Task GenerateAsync_NoAdminAssignment_EmitsUserRoleAndNoPermissionClaims()
    {
        var user = NewUser();
        var resolver = ResolverReturning(new AdminAuthority(AuthRoles.User, []));

        var result = await CreateGenerator(resolver).GenerateAsync(user, default);

        var principal = ReadToken(result.Token);
        Assert.Equal(user.Id.ToString(), principal.FindFirst(AuthClaimTypes.UserId)?.Value);
        Assert.Equal(user.SteamId, principal.FindFirst(AuthClaimTypes.SteamId)?.Value);
        Assert.Equal(AuthRoles.User, principal.FindFirst(AuthClaimTypes.Role)?.Value);
        Assert.Empty(principal.FindAll(AuthClaimTypes.Permission));
    }

    [Fact]
    public async Task GenerateAsync_SuperAdmin_EmitsSuperAdminRoleAndNoPermissionClaims()
    {
        var user = NewUser();
        var resolver = ResolverReturning(new AdminAuthority(AuthRoles.SuperAdmin, []));

        var result = await CreateGenerator(resolver).GenerateAsync(user, default);

        var principal = ReadToken(result.Token);
        Assert.Equal(AuthRoles.SuperAdmin, principal.FindFirst(AuthClaimTypes.Role)?.Value);
        // Handler short-circuits on super_admin so permission claims would be
        // wasted bytes — generator must not emit them.
        Assert.Empty(principal.FindAll(AuthClaimTypes.Permission));
    }

    [Fact]
    public async Task GenerateAsync_RegularAdmin_EmitsAdminRoleAndOnePermissionClaimPerEntry()
    {
        var user = NewUser();
        var permissions = new[] { "VIEW_FLAGS", "MANAGE_FLAGS", "VIEW_AUDIT_LOG" };
        var resolver = ResolverReturning(new AdminAuthority(AuthRoles.Admin, permissions));

        var result = await CreateGenerator(resolver).GenerateAsync(user, default);

        var principal = ReadToken(result.Token);
        Assert.Equal(AuthRoles.Admin, principal.FindFirst(AuthClaimTypes.Role)?.Value);
        var permissionValues = principal.FindAll(AuthClaimTypes.Permission)
            .Select(c => c.Value).ToList();
        Assert.Equal(permissions.Length, permissionValues.Count);
        Assert.All(permissions, p => Assert.Contains(p, permissionValues));
    }

    [Fact]
    public async Task GenerateAsync_ResolverIsCalledWithUserId()
    {
        var user = NewUser();
        var resolver = new Mock<IAdminAuthorityResolver>();
        resolver.Setup(r => r.ResolveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminAuthority(AuthRoles.User, []));

        await CreateGenerator(resolver.Object).GenerateAsync(user, default);

        resolver.Verify(r => r.ResolveAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        SteamId = "76561198000000001",
        SteamDisplayName = "Unit",
    };

    private static IAdminAuthorityResolver ResolverReturning(AdminAuthority authority)
    {
        var resolver = new Mock<IAdminAuthorityResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authority);
        return resolver.Object;
    }

    private static System.Security.Claims.ClaimsPrincipal ReadToken(string token)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateLifetime = true,
        }, out _);
    }
}
