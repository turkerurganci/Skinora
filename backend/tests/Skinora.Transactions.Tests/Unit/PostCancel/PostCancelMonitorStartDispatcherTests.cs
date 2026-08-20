using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PostCancel;

namespace Skinora.Transactions.Tests.Unit.PostCancel;

/// <summary>
/// T139 — the cancel-path handover. T75 registered the gradual-cadence monitor
/// but never dropped the active one, which was harmless only because nothing
/// armed the active monitor in the first place. Now that T139 arms it, the
/// handover must be a move, not a copy.
/// </summary>
public class PostCancelMonitorStartDispatcherTests
{
    private const string DepositAddress = "TPostCancelHandoverAddrFakeXXXXXXX";

    [Fact]
    public async Task Handover_Stops_The_Active_Monitor_Before_Starting_PostCancel()
    {
        var sidecar = new OrderRecordingSidecarClient();

        await BuildSut(sidecar).Handle(BuildEvent(), CancellationToken.None);

        // Order matters: if post-cancel were registered first, both registries
        // would be polling the same address for the length of the stop call.
        Assert.Equal(
            new[] { $"stop:{DepositAddress}", $"post-cancel-start:{DepositAddress}" },
            sidecar.Log);
    }

    [Fact]
    public async Task A_Failed_Stop_Does_Not_Abort_The_PostCancel_Registration()
    {
        // The post-cancel registration is the leg that carries the 08 §3.4
        // money-recovery guarantee for a late payment. Losing it because the
        // best-effort cleanup of the active monitor failed would trade a real
        // guarantee for a housekeeping one.
        var sidecar = new OrderRecordingSidecarClient
        {
            StopResult = BlockchainSidecarStatus.Unavailable,
        };

        await BuildSut(sidecar).Handle(BuildEvent(), CancellationToken.None);

        Assert.Contains($"post-cancel-start:{DepositAddress}", sidecar.Log);
    }

    [Fact]
    public async Task An_Unavailable_PostCancel_Start_Still_Throws_For_The_Outbox()
    {
        var sidecar = new OrderRecordingSidecarClient
        {
            PostCancelStartResult = BlockchainSidecarStatus.Unavailable,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildSut(sidecar).Handle(BuildEvent(), CancellationToken.None));
    }

    private static PostCancelMonitorStartDispatcher BuildSut(IBlockchainSidecarClient sidecar)
        => new(sidecar, NullLogger<PostCancelMonitorStartDispatcher>.Instance);

    private static PostCancelMonitorStartRequestedEvent BuildEvent()
        => new(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            PaymentAddressId: Guid.NewGuid(),
            Address: DepositAddress,
            ExpectedToken: StablecoinType.USDT,
            ExpectedContractAddress: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            CancelledAt: new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc),
            OccurredAt: new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc));

    /// <summary>
    /// Records call order across the two endpoints — the ordering claim cannot
    /// be made with two independent call lists.
    /// </summary>
    private sealed class OrderRecordingSidecarClient : IBlockchainSidecarClient
    {
        public List<string> Log { get; } = new();

        public BlockchainSidecarStatus StopResult { get; init; } =
            BlockchainSidecarStatus.Success;

        public BlockchainSidecarStatus PostCancelStartResult { get; init; } =
            BlockchainSidecarStatus.Success;

        public Task<BlockchainSidecarStatus> StopMonitoringAsync(
            string address, CancellationToken cancellationToken)
        {
            Log.Add($"stop:{address}");
            return Task.FromResult(StopResult);
        }

        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
            PostCancelMonitorStartRequest request, CancellationToken cancellationToken)
        {
            Log.Add($"post-cancel-start:{request.Address}");
            return Task.FromResult(PostCancelStartResult);
        }

        public Task<BlockchainSidecarStatus> StartMonitoringAsync(
            PaymentMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
            int index, Guid transactionId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
            IReadOnlyList<string> addresses, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
            HotToColdTransferRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
