using Skinora.Shared.Persistence;

namespace Skinora.Disputes.Infrastructure.Persistence;

public static class DisputesModuleDbRegistration
{
    public static void RegisterDisputesModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(DisputesModuleDbRegistration).Assembly);
    }
}
