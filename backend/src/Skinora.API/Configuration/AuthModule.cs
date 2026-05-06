using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Skinora.Auth.Authorization;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;

namespace Skinora.API.Configuration;

public static class AuthModule
{
    private static readonly JsonSerializerOptions ForbiddenJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };


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

            // T40 — On a 403 (authenticated but lacks the policy's permission
            // requirement) the default JwtBearer handler writes an empty body.
            // 07 §9 mandates a 403 INSUFFICIENT_PERMISSION envelope, so we
            // serialize the standard ApiResponse failure shape ourselves.
            // T61 — SignalR JS clients cannot set the Authorization header on
            // the WebSocket handshake, so 07 §11.1 documents JWT delivery via
            // a `?access_token=` query param. OnMessageReceived bridges the
            // query param into the bearer auth pipeline only for /hubs/*
            // requests so other endpoints keep enforcing header-only tokens.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken)
                        && path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";

                    var body = ApiResponse<object>.Fail(
                        "INSUFFICIENT_PERMISSION",
                        "You do not have permission to access this resource.",
                        traceId: context.HttpContext.TraceIdentifier);

                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(body, ForbiddenJsonOptions));
                },
            };
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
