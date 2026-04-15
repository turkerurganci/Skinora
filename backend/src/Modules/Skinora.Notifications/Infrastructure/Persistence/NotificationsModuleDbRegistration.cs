using Skinora.Shared.Persistence;

namespace Skinora.Notifications.Infrastructure.Persistence;

public static class NotificationsModuleDbRegistration
{
    public static void RegisterNotificationsModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(NotificationsModuleDbRegistration).Assembly);
    }
}
