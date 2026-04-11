using Skinora.Shared.Persistence;

namespace Skinora.Users.Infrastructure.Persistence;

public static class UsersModuleDbRegistration
{
    public static void RegisterUsersModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(UsersModuleDbRegistration).Assembly);
    }
}
