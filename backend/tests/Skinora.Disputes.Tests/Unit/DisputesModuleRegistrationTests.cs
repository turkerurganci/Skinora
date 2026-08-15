using Microsoft.Extensions.DependencyInjection;
using Skinora.Disputes.Application.Disputes;
using Skinora.Transactions.Application.Delivery;

namespace Skinora.Disputes.Tests.Unit;

/// <summary>
/// T127 — guards the one registration whose absence is invisible until
/// production.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IDeliveryMisdeliveryEscalator"/> is declared in
/// <c>Skinora.Transactions</c> and implemented here, because the module
/// dependency runs Disputes → Transactions. That split means the delivery
/// timeout round's dependency chain leaves its own assembly, and nothing else
/// in the test suite constructs it: the round runs inside
/// <c>DeadlineScannerJob</c>, a self-rescheduling Hangfire job that resolves
/// lazily. A missing adapter would therefore not fail a build, a unit test or
/// an endpoint test — it would fail the first time a delivery deadline expired
/// in production, and take the accept / seller-confirm / payment timeouts down
/// with it, since the same job enforces all four phases.
/// </para>
/// <para>
/// The other half of the chain — that <c>Program.cs</c> still calls
/// <see cref="DisputesModule.AddDisputesModule"/> at all — is already covered:
/// the API integration suite boots the real host, and the dispute endpoints
/// would not resolve without it.
/// </para>
/// </remarks>
public class DisputesModuleRegistrationTests
{
    [Fact]
    public void AddDisputesModule_Registers_The_Misdelivery_Escalation_Adapter()
    {
        var services = new ServiceCollection();

        services.AddDisputesModule();

        var descriptor = Assert.Single(
            services, d => d.ServiceType == typeof(IDeliveryMisdeliveryEscalator));
        Assert.Equal(typeof(MisdeliveryDisputeEscalator), descriptor.ImplementationType);
        // Scoped, like every other service in this module: the adapter writes
        // into the caller's AppDbContext, so sharing the caller's scope is the
        // whole contract (a singleton would capture a disposed context).
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
