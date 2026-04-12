using Skinora.Shared.Persistence;

namespace Skinora.Steam.Infrastructure.Persistence;

public static class SteamModuleDbRegistration
{
    public static void RegisterSteamModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(SteamModuleDbRegistration).Assembly);
    }
}
