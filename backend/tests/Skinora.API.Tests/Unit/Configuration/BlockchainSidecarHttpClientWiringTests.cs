using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.Configuration;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// #323 doğrulama turu 2 (bulgu D1) — the guard that makes "the broadcast
/// budget exists" and "the registered client actually uses it" the same
/// statement.
/// </summary>
/// <remarks>
/// <para>
/// A deposit-sourced broadcast waits on the sidecar for every resource step to
/// land in a block (08 §3.3): the sidecar refuses to broadcast a transfer more
/// than 150 s into the call and then waits up to ~75 s for the transfer's own
/// block before reclaiming delegated Energy. The backend must therefore hold
/// the call for <see cref="BlockchainSidecarOptions.DefaultTransferTimeoutSeconds"/>
/// seconds; if it gives up sooner it retries, the sidecar broadcasts anyway,
/// and the first transfer is never recorded against its row.
/// </para>
/// <para>
/// <see cref="BlockchainSidecarOptions.ResolveTransferTimeout"/> is covered by
/// <c>BlockchainSidecarTimeoutTests</c>, and the transfer client's own tests
/// build an <see cref="HttpClient"/> the same way <see cref="TransactionsModule"/>
/// does — which is precisely the hole this closes: both of those stay green
/// while the registration line hands the named client the 30 s chain-read
/// budget again (measured 2026-09-17, mutation survived 639 + 74 tests). The
/// wiring is only pinned where the container builds it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BlockchainSidecarHttpClientWiringTests
{
    [Fact]
    public void Transfer_Client_Gets_The_Broadcast_Budget_Not_The_Chain_Read_One()
    {
        var factory = BuildFactory(timeoutSeconds: "10");

        var transfer = factory.CreateClient(HttpBlockchainTransferClient.HttpClientName);
        var estimate = factory.CreateClient(HttpSidecarGasFeeEstimator.HttpClientName);
        var sidecar = factory.CreateClient(HttpBlockchainSidecarClient.HttpClientName);

        // The three budgets are deliberately different: only the broadcast
        // waits for blocks.
        Assert.Equal(TimeSpan.FromSeconds(300), transfer.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), estimate.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), sidecar.Timeout);
    }

    [Fact]
    public void Transfer_Client_Honours_An_Operator_Override()
    {
        var factory = BuildFactory(timeoutSeconds: "10", transferTimeoutSeconds: "420");

        var transfer = factory.CreateClient(HttpBlockchainTransferClient.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(420), transfer.Timeout);
        // The operator value must not leak into the reads.
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            factory.CreateClient(HttpSidecarGasFeeEstimator.HttpClientName).Timeout);
    }

    private static IHttpClientFactory BuildFactory(
        string timeoutSeconds,
        string? transferTimeoutSeconds = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{BlockchainSidecarOptions.SectionName}:BaseUrl"] = "http://sidecar:5200",
            [$"{BlockchainSidecarOptions.SectionName}:TimeoutSeconds"] = timeoutSeconds,
        };
        if (transferTimeoutSeconds is not null)
        {
            settings[$"{BlockchainSidecarOptions.SectionName}:TransferTimeoutSeconds"] =
                transferTimeoutSeconds;
        }

        var services = new ServiceCollection();
        services.AddTransactionsModule(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }
}
