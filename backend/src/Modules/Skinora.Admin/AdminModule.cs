using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skinora.Admin.Application.Notifications;
using Skinora.Admin.Application.Roles;
using Skinora.Admin.Application.Users;
using Skinora.Shared.Interfaces;

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

        // WP8 — admin-alert recipient resolution (broadcast to all admins).
        // Implemented here because the Admin module owns AdminUserRole; the
        // Notifications-module consumers depend only on the Shared abstraction.
        services.AddScoped<IAdminRecipientResolver, AdminRecipientResolver>();

        return services;
    }
}
