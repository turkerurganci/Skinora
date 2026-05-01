using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Admin.Application.Roles;
using Skinora.Admin.Application.Users;

namespace Skinora.Admin;

/// <summary>
/// DI wiring for the Skinora.Admin module — admin role + user management
/// services backing 07 §9.11–§9.18 (T39).
/// </summary>
public static class AdminModule
{
    public static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAdminRoleService, AdminRoleService>();
        services.AddScoped<IAdminUserService, AdminUserService>();

        return services;
    }
}
