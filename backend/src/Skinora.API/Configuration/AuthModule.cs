using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Skinora.Auth.Authorization;
using Skinora.Auth.Configuration;

namespace Skinora.API.Configuration;

public static class AuthModule
{
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind JwtSettings from configuration
        var jwtSection = configuration.GetSection(JwtSettings.SectionName);
        services.Configure<JwtSettings>(jwtSection);

        var jwtSettings = jwtSection.Get<JwtSettings>()
            ?? throw new InvalidOperationException("JWT configuration section is missing.");

        // Build signing keys (current + optional previous for rotation grace period)
        var currentKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
        var signingKeys = new List<SecurityKey> { currentKey };

        if (!string.IsNullOrWhiteSpace(jwtSettings.PreviousSecret))
        {
            signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.PreviousSecret)));
        }

        // JWT Bearer Authentication
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,

                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = currentKey,
                IssuerSigningKeys = signingKeys, // Supports key rotation grace period

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),

                RequireExpirationTime = true,
                RequireSignedTokens = true,
            };

            // Do not map standard claim types to .NET defaults
            options.MapInboundClaims = false;
        });

        // Authorization policies
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Authenticated, policy =>
                policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.AdminAccess, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(AuthClaimTypes.Role, AuthRoles.Admin, AuthRoles.SuperAdmin))
            .AddPolicy(AuthPolicies.SuperAdmin, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(AuthClaimTypes.Role, AuthRoles.SuperAdmin));

        // Dynamic permission-based policy provider
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }
}
