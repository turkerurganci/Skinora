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
/// <para>
/// <b>Counted per handler, not per notification type (T139 düzeltme turu 3 —
/// bulgu N1-3).</b> The first version of this guard compared the <em>set</em> of
/// declared <c>INotificationHandler&lt;T&gt;</c> interfaces against the set of
/// registered ones. That closes the case that actually happened (a new event
/// with a new handler and no registration) but not its sibling: a
/// <em>second</em> handler for an event type that already has a registered one
/// collapses into the same interface entry, so the guard stayed green while the
/// new handler was just as unreachable. Both halves below are therefore
/// counted, not merely matched — the registration count per notification type
/// has to keep up with the number of handlers declared for it, and the concrete
/// type has to be registered too, because the module's idiom forwards the
/// interface to it (<c>AddScoped&lt;X&gt;()</c> +
/// <c>AddScoped&lt;INotificationHandler&lt;T&gt;&gt;(sp =&gt;
/// sp.GetRequiredService&lt;X&gt;())</c>) and a missing concrete line turns the
/// factory into a resolve-time throw.
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

        var registrationCountByNotification = services
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        var registeredServiceTypes = services.Select(d => d.ServiceType).ToHashSet();

        // One entry per (handler type, notification it handles) pair — NOT
        // deduplicated by notification, which is the whole point of N1-3.
        var declared = typeof(PaymentMonitorStartDispatcher).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                .Select(i => (Handler: t, Notification: i)))
            .ToList();

        Assert.NotEmpty(declared);

        var problems = new List<string>();

        foreach (var byNotification in declared.GroupBy(d => d.Notification))
        {
            registrationCountByNotification.TryGetValue(byNotification.Key, out var registered);
            var declaredCount = byNotification.Count();
            if (registered < declaredCount)
            {
                problems.Add(
                    $"{byNotification.Key.GenericTypeArguments[0].Name}: {declaredCount} handler(s) "
                    + $"declared ({string.Join(" + ", byNotification.Select(d => d.Handler.Name))}) "
                    + $"but only {registered} INotificationHandler<> registration(s)");
            }
        }

        foreach (var handlerType in declared.Select(d => d.Handler).Distinct())
        {
            if (!registeredServiceTypes.Contains(handlerType))
            {
                problems.Add($"{handlerType.Name}: the concrete type itself is not registered");
            }
        }

        Assert.True(
            problems.Count == 0,
            "The Transactions assembly is not in the OutboxModule MediatR scan list, so each "
            + "INotificationHandler<T> it declares must be registered explicitly in "
            + "TransactionsModule. Unregistered: "
            + string.Join(" | ", problems));
    }
}
