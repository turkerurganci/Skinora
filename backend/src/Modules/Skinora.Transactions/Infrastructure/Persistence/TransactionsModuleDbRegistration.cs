using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Infrastructure.Persistence;

public static class TransactionsModuleDbRegistration
{
    public static void RegisterTransactionsModule()
    {
        AppDbContext.RegisterModuleAssembly(typeof(TransactionsModuleDbRegistration).Assembly);
    }
}
