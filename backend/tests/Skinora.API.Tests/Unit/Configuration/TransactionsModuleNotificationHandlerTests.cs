using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.Configuration;
using Skinora.Transactions.Application.PaymentMonitoring;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T139 düzeltme turu (bulgu B1) — the guard that makes "the handler exists"
/// and "the handler is reachable" the same statement for the Transactions
/// module.
/// </summary>
/// <remarks>
/// <para>
/// <c>OutboxModule</c> scans exactly three assemblies for MediatR handlers
/// (the API host, Notifications, Realtime). <c>Skinora.Transactions</c> is not
/// among them, so every <see cref="INotificationHandler{TNotification}"/> it
/// declares has to be registered by hand in <see cref="TransactionsModule"/>.
/// Nothing enforced that: a handler written without its registration line
/// compiles, unit-tests green (its tests construct it directly), and then
/// silently never runs — <c>IPublisher.Publish</c> with zero handlers returns
/// normally and the outbox stamps the row PROCESSED.
/// </para>
/// <para>
/// That is exactly how <see cref="PaymentMonitorStartDispatcher"/> shipped
/// unregistered in the T139 build round, and it is the same defect class T139
/// itself was created to close (an endpoint with no caller). The test is
/// written over the whole assembly rather than over that one handler, because
/// the next handler added here would otherwise repeat it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TransactionsModuleNotificationHandlerTests
{
    [Fact]
    public void Every_Transactions_Module_Notification_Handler_Is_Registered()
    {
        var services = new ServiceCollection();
        services.AddTransactionsModule(
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        var registered = services
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .ToHashSet();

        var declared = typeof(PaymentMonitorStartDispatcher).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Distinct()
            .ToList();

        Assert.NotEmpty(declared);

        var missing = declared.Where(i => !registered.Contains(i)).ToList();

        Assert.True(
            missing.Count == 0,
            "The Transactions assembly is not in the OutboxModule MediatR scan list, so each "
            + "INotificationHandler<T> it declares must be registered explicitly in "
            + "TransactionsModule. Unregistered: "
            + string.Join(", ", missing.Select(i => i.GenericTypeArguments[0].Name)));
    }
}
