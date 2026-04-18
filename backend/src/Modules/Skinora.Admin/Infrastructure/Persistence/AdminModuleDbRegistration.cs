using Skinora.Shared.Persistence;

namespace Skinora.Admin.Infrastructure.Persistence;

public static class AdminModuleDbRegistration
{
    public static void RegisterAdminModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(AdminModuleDbRegistration).Assembly);
    }
}
