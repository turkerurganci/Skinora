using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Contract;

/// <summary>
/// Pins the backend half of the blockchain sidecar's
/// <c>POST /api/monitor/start</c> request against the example bodies the
/// sidecar's handler test replays (<c>sidecar-blockchain/contracts/monitor-start/</c>).
/// </summary>
/// <remarks>
/// T139-ActiveMonitorQuotaAlarm (owner decision 2026-10-02) added the
/// <c>cadence</c> field: PAYMENT while the buyer's payment is awaited, HOLDING
/// once it is confirmed (08 §3.4). The sidecar arms an address whose body names
/// no cadence at PAYMENT and rejects any spelling but these two with 400 — so a
/// backend that dropped the field would quietly keep every address at 3 s and
/// spend the quota the field exists to save, and one that misspelt it would
/// stop arming at all. Both failures are invisible to a test that only reads
/// its own serializer; this one reads the sidecar's files.
/// </remarks>
public sealed class BlockchainSidecarMonitorStartContractTests
{
    private const string SidecarBaseUrl = "http://blockchain-sidecar.test/";

    [Theory]
    [InlineData(PaymentMonitorCadence.Payment, "payment.request.json")]
    [InlineData(PaymentMonitorCadence.Holding, "holding.request.json")]
    public async Task StartRequest_IsTheBodyTheSidecarAccepts(
        PaymentMonitorCadence cadence, string exampleFile)
    {
        var expected = LoadExample(exampleFile);
        string? sentBody = null;
        var handler = new CapturingHandler(async request =>
        {
            sentBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new HttpBlockchainSidecarClient(
            new HttpClient(handler) { BaseAddress = new Uri(SidecarBaseUrl) },
            Options.Create(new BlockchainSidecarOptions { BaseUrl = SidecarBaseUrl }),
            NullLogger<HttpBlockchainSidecarClient>.Instance);

        await client.StartMonitoringAsync(
            new PaymentMonitorStartRequest(
                Address: Text(expected, "address"),
                PaymentAddressId: Guid.Parse(Text(expected, "paymentAddressId")),
                TransactionId: Guid.Parse(Text(expected, "transactionId")),
                ExpectedContract: Text(expected, "expectedContract"),
                ExpectedSymbol: Text(expected, "expectedSymbol"),
                Cadence: cadence),
            CancellationToken.None);

        Assert.NotNull(sentBody);
        Assert.True(
            JsonNode.DeepEquals(expected, JsonNode.Parse(sentBody!)),
            $"Sent {sentBody}, the sidecar's example is {expected.ToJsonString()}");
    }

    private static JsonNode LoadExample(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "sidecar-blockchain", "contracts", "monitor-start", fileName);
        return JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty.");
    }

    private static string Text(JsonNode body, string key) =>
        body[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"Example body has no '{key}'.");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root (docker-compose.yml) not found.");
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
