using Skinora.Shared.Persistence;

namespace Skinora.Auth.Infrastructure.Persistence;

public static class AuthModuleDbRegistration
{
    public static void RegisterAuthModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(AuthModuleDbRegistration).Assembly);
    }
}
