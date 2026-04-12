using Skinora.Shared.Persistence;

namespace Skinora.Fraud.Infrastructure.Persistence;

public static class FraudModuleDbRegistration
{
    public static void RegisterFraudModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(FraudModuleDbRegistration).Assembly);
    }
}
