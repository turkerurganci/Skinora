using Skinora.Shared.Persistence;

namespace Skinora.Platform.Infrastructure.Persistence;

public static class PlatformModuleDbRegistration
{
    public static void RegisterPlatformModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(PlatformModuleDbRegistration).Assembly);
    }
}
