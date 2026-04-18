using Skinora.Shared.Persistence;

namespace Skinora.Payments.Infrastructure.Persistence;

public static class PaymentsModuleDbRegistration
{
    public static void RegisterPaymentsModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(PaymentsModuleDbRegistration).Assembly);
    }
}
