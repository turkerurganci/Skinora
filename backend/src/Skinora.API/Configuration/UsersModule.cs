using Skinora.Users.Application.Profiles;

namespace Skinora.API.Configuration;

/// <summary>
/// DI registration for the Skinora.Users module — profile read services
/// backing 07 §5.1 (U1), §5.2 (U2), §5.5 (U5).
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        services.AddScoped<IUserProfileService, UserProfileService>();

        return services;
    }
}
