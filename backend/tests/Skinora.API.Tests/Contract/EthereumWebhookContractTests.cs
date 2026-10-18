using System.Globalization;
using System.Text.Json;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.API.Tests.Contract;

/// <summary>
/// T151 — the backend half of the Ethereum service's webhook bodies
/// (<c>sidecar-ethereum/contracts/webhook/*.json</c>). The service's
/// <c>WebhookPayloads.test.ts</c> builds the same files through the function
/// every event is emitted with; this test binds them into the controller's
/// envelope types (ASP.NET Core web JSON defaults) and reads the network the
/// way the controller does. Ethereum carries the chain's own log index
/// (non-zero here on purpose) and a 0x transaction hash.
/// </summary>
public sealed class EthereumWebhookContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Examples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ContractDir(), "*.json").OrderBy(f => f, StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_Example_Names_Ethereum(string file)
    {
        var envelope = JsonSerializer.Deserialize<BlockchainWebhookEnvelope<JsonElement>>(
            File.ReadAllText(Path.Combine(ContractDir(), file)), WebJson)!;

        Assert.True(BlockchainWebhookHandler.TryParseNetwork(envelope.Network, out var network), file);
        Assert.Equal(BlockchainNetwork.ETHEREUM, network);
    }

    [Fact]
    public void Examples_Bind_To_The_Controllers_Envelope_Types()
    {
        var detected = Load<PaymentDetectedData>("payment-detected.json");
        Assert.Equal(BlockchainWebhookEvents.PaymentDetected, detected.Event);
        Assert.StartsWith("0x", detected.Data.TxHash);
        Assert.Equal(2, detected.Data.EventIndex);
        Assert.Equal("USDT", detected.Data.TokenSymbol);
        Assert.Equal("0xdac17f958d2ee523a2206206994597c13d831ec7", detected.Data.ContractAddress);
        Assert.Equal(20.4m, decimal.Parse(detected.Data.Amount, CultureInfo.InvariantCulture));

        var confirmed = Load<PaymentConfirmedData>("payment-confirmed.json");
        Assert.Equal(detected.Data.TxHash, confirmed.Data.TxHash);
        Assert.Equal(detected.Data.EventIndex, confirmed.Data.EventIndex);
        Assert.Equal(64, confirmed.Data.ConfirmationCount);
        Assert.True(confirmed.Data.BlockNumber > 0);

        var wrong = Load<WrongTokenIncomingData>("wrong-token.json");
        Assert.Equal("USDC", wrong.Data.ActualTokenSymbol);

        var spam = Load<SpamTokenIncomingData>("spam-token.json");
        // Spam amounts are written with 18 decimals (the contract is never called).
        Assert.True(decimal.TryParse(spam.Data.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount));
        Assert.Equal(1000m, amount);

        var late = Load<LatePaymentDetectedData>("late-payment-detected.json");
        Assert.Equal(PostCancelMonitorStates.PostCancel24h, late.Data.MonitorState);

        var state = Load<PostCancelMonitorStateChangedData>("post-cancel-monitor-state-changed.json");
        Assert.Equal(PostCancelMonitorStates.PostCancel7d, state.Data.NewState);
    }

    private static BlockchainWebhookEnvelope<T> Load<T>(string file) =>
        JsonSerializer.Deserialize<BlockchainWebhookEnvelope<T>>(
            File.ReadAllText(Path.Combine(ContractDir(), file)), WebJson)!;

    private static string ContractDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
                return Path.Combine(dir.FullName, "sidecar-ethereum", "contracts", "webhook");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root (docker-compose.yml) not found.");
    }
}
