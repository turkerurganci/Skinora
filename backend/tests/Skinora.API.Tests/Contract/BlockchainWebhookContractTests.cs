using System.Text.Json;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.API.Tests.Contract;

/// <summary>
/// T148 (05 §3.3a "Webhook yükü") — pins the backend half of the blockchain
/// webhook envelope against <c>sidecar-blockchain/contracts/webhook/*.json</c>.
/// The sidecar's <c>WebhookPayloads.test.ts</c> builds the same files through the
/// function both of its registries emit with; this test deserializes them the
/// way the controller does (ASP.NET Core's web JSON defaults) into the
/// controller's envelope types and reads the network the way the controller
/// does. A rename of <c>network</c> — or of any field the handler reads — on
/// either side fails one of the two tests.
/// </summary>
public sealed class BlockchainWebhookContractTests
{
    // MVC binds request bodies with JsonSerializerDefaults.Web.
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
    public void Every_Example_Names_A_Network_The_Controller_Accepts(string file)
    {
        var json = File.ReadAllText(Path.Combine(ContractDir(), file));
        var envelope = JsonSerializer.Deserialize<BlockchainWebhookEnvelope<JsonElement>>(json, WebJson)!;

        Assert.True(
            BlockchainWebhookHandler.TryParseNetwork(envelope.Network, out var network),
            $"{file}: network '{envelope.Network}' is not one the controller accepts.");
        Assert.Equal(BlockchainNetwork.TRON, network);
    }

    [Fact]
    public void Examples_Bind_To_The_Controllers_Envelope_Types()
    {
        var detected = Load<PaymentDetectedData>("payment-detected.json");
        Assert.Equal(BlockchainWebhookEvents.PaymentDetected, detected.Event);
        Assert.NotEqual(Guid.Empty, detected.Data.PaymentAddressId);
        Assert.False(string.IsNullOrEmpty(detected.Data.TxHash));
        Assert.Equal("USDT", detected.Data.TokenSymbol);
        Assert.Equal("20.400000", detected.Data.Amount);

        var confirmed = Load<PaymentConfirmedData>("payment-confirmed.json");
        Assert.Equal(BlockchainWebhookEvents.PaymentConfirmed, confirmed.Event);
        Assert.Equal(20, confirmed.Data.ConfirmationCount);
        Assert.True(confirmed.Data.BlockNumber > 0);

        var wrong = Load<WrongTokenIncomingData>("wrong-token.json");
        Assert.Equal(BlockchainWebhookEvents.WrongTokenIncoming, wrong.Event);
        Assert.Equal("USDC", wrong.Data.ActualTokenSymbol);
        Assert.False(string.IsNullOrEmpty(wrong.Data.ActualContractAddress));

        var spam = Load<SpamTokenIncomingData>("spam-token.json");
        Assert.Equal(BlockchainWebhookEvents.SpamTokenIncoming, spam.Event);
        Assert.False(string.IsNullOrEmpty(spam.Data.ActualContractAddress));

        var late = Load<LatePaymentDetectedData>("late-payment-detected.json");
        Assert.Equal(BlockchainWebhookEvents.LatePaymentDetected, late.Event);
        Assert.Equal(PostCancelMonitorStates.PostCancel24h, late.Data.MonitorState);

        var state = Load<PostCancelMonitorStateChangedData>("post-cancel-monitor-state-changed.json");
        Assert.Equal(BlockchainWebhookEvents.PostCancelMonitorStateChanged, state.Event);
        Assert.Equal(PostCancelMonitorStates.PostCancel7d, state.Data.NewState);
    }

    [Theory]
    [InlineData("ETHEREUM", true)]
    [InlineData("TRON", true)]
    [InlineData("tron", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Network_Is_Parsed_By_Exact_Name_Only(string? wire, bool accepted)
    {
        Assert.Equal(accepted, BlockchainWebhookHandler.TryParseNetwork(wire, out _));
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
                return Path.Combine(dir.FullName, "sidecar-blockchain", "contracts", "webhook");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root (docker-compose.yml) not found.");
    }
}
