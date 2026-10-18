using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Outbox;
using Skinora.API.Services.WalletClosures;
using Skinora.API.Tests.Common;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PaymentMonitoring;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T157 — the two-person tool that closes an Ethereum outbound row with an
/// on-chain hash after the signing service lost its journal (07 §9.32,
/// DEPLOY_RUNBOOK §D adım 7.4). ⚠ PARA. Runs on the migrated SQL Server
/// schema so the CHECKs and the filtered unique indexes of the T157 migration
/// are part of what is tested. Each rule of the tool has a test that fails
/// when the rule is removed (mutation list: 11 §F8 T157 kararları).
/// </summary>
public class OutboundRowClosureTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static OutboundRowClosureTests() => RegisterAllModules();

    private const string Hash = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Hash2 = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string GoodReason = "Nonce 17 of the hot wallet matches this payout: same recipient and amount on Etherscan.";

    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly ChainStub _chain = new();
    private User _seller = null!;
    private User _alice = null!;
    private User _bob = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = NewUser("76561198000001001", "T157 Seller");
        _alice = NewUser("76561198000001002", "T157 Alice");
        _bob = NewUser("76561198000001003", "T157 Bob");
        context.Set<User>().AddRange(_seller, _alice, _bob);
        await context.SaveChangesAsync();
    }

    private static User NewUser(string steamId, string name) =>
        new() { Id = Guid.NewGuid(), SteamId = steamId, SteamDisplayName = name };

    private OutboundRowClosureService Service(AppDbContext ctx)
    {
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, transfers: new ChainStub())
            .With(BlockchainNetwork.ETHEREUM, transfers: _chain);
        var wallets = new StubPlatformWalletAddressProvider();
        wallets.Pairs[BlockchainNetwork.ETHEREUM] =
            new PlatformWalletAddresses(BlockchainNetwork.ETHEREUM, HotWallet, EvmLower2);
        return new OutboundRowClosureService(
            ctx, registry, wallets, _clock, NullLogger<OutboundRowClosureService>.Instance);
    }

    private const string HotWallet = "0x1111111111111111111111111111111111111111";

    private async Task<BlockchainTransaction> SeedRowAsync(
        BlockchainNetwork network = BlockchainNetwork.ETHEREUM,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.PENDING,
        BlockchainTransactionType type = BlockchainTransactionType.SELLER_PAYOUT,
        string? txHash = null)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: network);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();

        PaymentAddress? deposit = null;
        if (type == BlockchainTransactionType.BUYER_PAYMENT)
        {
            deposit = NewPaymentAddress(tx.Id, network, EvmChecksummed2, 1);
            ctx.Set<PaymentAddress>().Add(deposit);
            await ctx.SaveChangesAsync();
        }

        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = deposit?.Id,
            Network = network,
            Type = type,
            TxHash = txHash,
            EventIndex = type == BlockchainTransactionType.BUYER_PAYMENT ? 0 : null,
            FromAddress = network == BlockchainNetwork.TRON ? TronAddress : EvmChecksummed2,
            ToAddress = network == BlockchainNetwork.TRON ? SellerWallet : EvmLower,
            Amount = 50m,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? Start.UtcDateTime : null,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED && network == BlockchainNetwork.TRON ? 20 : 0,
            CreatedAt = Start.UtcDateTime,
        };
        ctx.Set<BlockchainTransaction>().Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    private async Task<ClosureDto> ProposeOkAsync(Guid rowId, string hash = Hash, Guid? admin = null)
    {
        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            admin ?? _alice.Id, rowId, hash, GoodReason, "10.0.0.1", CancellationToken.None);
        Assert.Null(result.Error);
        return result.Value!;
    }

    private async Task<ClosureResult> ApproveAsync(Guid closureId, Guid admin, string? hash = Hash)
    {
        await using var ctx = CreateContext();
        return await Service(ctx).ApproveAsync(admin, closureId, hash, "10.0.0.2", CancellationToken.None);
    }

    private async Task<BlockchainTransaction> ReloadRowAsync(Guid id)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<List<AuditLog>> AuditAsync(params AuditAction[] actions)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<AuditLog>().AsNoTracking()
            .Where(a => actions.Contains(a.Action)).OrderBy(a => a.Id).ToListAsync();
    }

    private static readonly AuditAction[] ClosureActions =
    [
        AuditAction.OUTBOUND_ROW_CLOSURE_PROPOSED,
        AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED,
        AuditAction.OUTBOUND_ROW_CLOSURE_REJECTED,
        AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED,
    ];

    // =================================================================
    // Propose
    // =================================================================

    [Fact]
    public async Task Propose_WritesAProposedClosure_AuditsIt_AndLeavesTheRowUntouched()
    {
        var row = await SeedRowAsync();

        var closure = await ProposeOkAsync(row.Id, Hash.ToUpperInvariant().Replace("0X", "0x"));

        Assert.Equal(OutboundRowClosureStatus.PROPOSED, closure.Status);
        Assert.Equal(Hash, closure.TxHash); // canonical lowercase
        Assert.Equal(_alice.Id, closure.ProposedByAdminId);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);

        var audit = Assert.Single(await AuditAsync(ClosureActions));
        Assert.Equal(AuditAction.OUTBOUND_ROW_CLOSURE_PROPOSED, audit.Action);
        Assert.Equal(_alice.Id, audit.ActorId);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.Equal(closure.Id.ToString("D"), audit.EntityId);
        Assert.Contains(Hash, audit.NewValue);
        Assert.Contains(row.Id.ToString(), audit.NewValue);
        Assert.Empty(_chain.StatusReads); // proposing asks nothing and sends nothing
        Assert.Empty(_chain.Broadcasts);
    }

    [Fact]
    public async Task Propose_ForATronRow_IsRefused()
    {
        var row = await SeedRowAsync(network: BlockchainNetwork.TRON);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowNotEthereum, result.Error);
        Assert.Empty(await AuditAsync(ClosureActions));
        await using var verify = CreateContext();
        Assert.Equal(0, await verify.Set<OutboundRowClosure>().CountAsync());
    }

    [Fact]
    public async Task Propose_ForAnInboundRow_IsRefused()
    {
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.BUYER_PAYMENT,
            status: BlockchainTransactionStatus.DETECTED);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        // DETECTED is also "not open"; the type check comes first so the answer names the real reason.
        Assert.Equal(ClosureError.RowNotOutbound, result.Error);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    [InlineData(BlockchainTransactionStatus.CONFIRMED)]
    [InlineData(BlockchainTransactionStatus.FAILED)]
    public async Task Propose_ForARowThatIsNotPending_IsRefused(BlockchainTransactionStatus status)
    {
        var row = await SeedRowAsync(status: status, txHash: Hash2);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowNotOpen, result.Error);
        Assert.Empty(await AuditAsync(ClosureActions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x1234")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]            // no 0x
    [InlineData("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]            // 63 digits
    [InlineData("0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]          // 65 digits
    [InlineData("0xgggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]           // not hex
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6tTR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]        // TRON-looking
    public async Task Propose_WithAMalformedHash_IsRefused(string hash)
    {
        var row = await SeedRowAsync();

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.InvalidHash, result.Error);
        Assert.Empty(await AuditAsync(ClosureActions));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too short")]
    public async Task Propose_WithoutARealReason_IsRefused(string? reason)
    {
        var row = await SeedRowAsync();

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, reason, null, CancellationToken.None);

        Assert.Equal(ClosureError.Validation, result.Error);
    }

    [Fact]
    public async Task Propose_ForAnUnknownRow_Is404()
    {
        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, Guid.NewGuid(), Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowNotFound, result.Error);
    }

    [Fact]
    public async Task Propose_AHashAlreadyOnAnotherRow_IsRefused()
    {
        await SeedRowAsync(status: BlockchainTransactionStatus.DETECTED, txHash: Hash);
        var open = await SeedRowAsync();

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, open.Id, Hash.ToUpperInvariant().Replace("0X", "0x"), GoodReason, null,
            CancellationToken.None);

        Assert.Equal(ClosureError.HashAlreadyBound, result.Error);
    }

    [Fact]
    public async Task Propose_AHashAlreadyInALiveClosure_IsRefused_ButAHashOfARejectedOneCanBeReused()
    {
        var first = await SeedRowAsync();
        var second = await SeedRowAsync();
        var closure = await ProposeOkAsync(first.Id, Hash);

        await using (var ctx = CreateContext())
        {
            var again = await Service(ctx).ProposeAsync(
                _bob.Id, second.Id, Hash, GoodReason, null, CancellationToken.None);
            Assert.Equal(ClosureError.HashAlreadyBound, again.Error);
        }

        await using (var ctx = CreateContext())
        {
            var rejected = await Service(ctx).RejectAsync(
                _bob.Id, closure.Id, "Wrong row: the amount differs.", null, CancellationToken.None);
            Assert.Null(rejected.Error);
        }

        var reused = await ProposeOkAsync(second.Id, Hash, _bob.Id);
        Assert.Equal(OutboundRowClosureStatus.PROPOSED, reused.Status);
    }

    [Fact]
    public async Task Propose_ASecondLiveClosureForTheSameRow_IsRefused()
    {
        var row = await SeedRowAsync();
        await ProposeOkAsync(row.Id, Hash);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _bob.Id, row.Id, Hash2, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.OpenClosureExists, result.Error);
    }

    // =================================================================
    // Approve
    // =================================================================

    [Fact]
    public async Task Approve_ByADifferentAdmin_BindsTheHash_MovesTheRowToDetected_AndSendsNothing()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Null(result.Error);
        Assert.Equal(OutboundRowClosureStatus.APPROVED, result.Value!.Status);
        Assert.Equal(_bob.Id, result.Value.DecidedByAdminId);

        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(Hash, after.TxHash);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, after.Status);
        Assert.Equal(0, after.ConfirmationCount);
        Assert.Null(after.NextAttemptAt);

        // The tool asked the chain about exactly this hash and sent nothing.
        Assert.Equal(Hash, Assert.Single(_chain.StatusReads));
        Assert.Empty(_chain.Broadcasts);

        var audits = await AuditAsync(ClosureActions);
        Assert.Equal(
            [AuditAction.OUTBOUND_ROW_CLOSURE_PROPOSED, AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED],
            audits.Select(a => a.Action).ToArray());
        var approved = audits[1];
        Assert.Equal(_bob.Id, approved.ActorId);
        Assert.Contains(_alice.Id.ToString(), approved.NewValue);
        Assert.Contains("SUCCESS", approved.NewValue);
    }

    [Fact]
    public async Task Approve_ByTheProposer_IsRefused_RowUntouched_AndLogged()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id, admin: _alice.Id);
        _chain.Status = Final();

        var result = await ApproveAsync(closure.Id, _alice.Id);

        Assert.Equal(ClosureError.SelfApproval, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        Assert.Empty(_chain.StatusReads); // refused before the chain was even asked

        await using var ctx = CreateContext();
        var stored = await ctx.Set<OutboundRowClosure>().AsNoTracking().SingleAsync();
        Assert.Equal(OutboundRowClosureStatus.PROPOSED, stored.Status);
        var refused = Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED));
        Assert.Equal(_alice.Id, refused.ActorId);
        Assert.Contains("SELF_APPROVAL", refused.NewValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("not a hash")]
    public async Task Approve_WithoutRetypingTheProposedHash_IsRefused_RowUntouched(string? typed)
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        var result = await ApproveAsync(closure.Id, _bob.Id, typed);

        Assert.Equal(ClosureError.HashMismatch, result.Error);
        Assert.Equal(BlockchainTransactionStatus.PENDING, (await ReloadRowAsync(row.Id)).Status);
        Assert.Empty(_chain.StatusReads);
        var refused = Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED));
        Assert.Contains("HASH_CONFIRMATION_MISMATCH", refused.NewValue);
    }

    [Fact]
    public async Task Approve_AcceptsTheRetypedHashInAnyLetterCase()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id, "0xABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD");
        _chain.Status = Final();

        var result = await ApproveAsync(closure.Id, _bob.Id,
            "0xabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd");

        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(TransferStatusOutcome.Pending, ClosureError.HashNotFinal)]
    [InlineData(TransferStatusOutcome.Failed, ClosureError.HashNotSuccessful)]
    [InlineData(TransferStatusOutcome.Rejected, ClosureError.HashNotFound)]
    [InlineData(TransferStatusOutcome.Unavailable, ClosureError.ChainUnavailable)]
    public async Task Approve_UnlessTheChainShowsTheHashFinalAndSuccessful_RefusesAndChangesNothing(
        TransferStatusOutcome outcome, ClosureError expected)
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = new TransferStatusResult(outcome, null, null, "REVERT", "x");

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(expected, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        await using var ctx = CreateContext();
        Assert.Equal(OutboundRowClosureStatus.PROPOSED,
            (await ctx.Set<OutboundRowClosure>().AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Approve_AsksTheServiceToVerifyTheHashAgainstTheRow_PayoutFromTheHotWallet()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);

        var asked = Assert.Single(_chain.VerifyReads);
        Assert.Equal(Hash, asked.TxHash);
        Assert.Equal(StablecoinType.USDT, asked.Token);
        Assert.Equal(row.Amount, asked.Amount);
        Assert.Equal(row.ToAddress, asked.To, ignoreCase: true);
        // A payout leaves the hot wallet — the row does not say so, the deployment does.
        Assert.Equal(HotWallet, asked.From);
    }

    [Fact]
    public async Task Approve_ARefundMustHaveLeftTheDepositTheRowNames()
    {
        var row = await SeedRowAsync(type: BlockchainTransactionType.BUYER_REFUND);
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);

        Assert.Equal(row.FromAddress, Assert.Single(_chain.VerifyReads).From, ignoreCase: true);
    }

    [Theory]
    [InlineData(TransferVerifyOutcome.NoMatchingTransfer, ClosureError.HashDoesNotMatchRow)]
    [InlineData(TransferVerifyOutcome.Reverted, ClosureError.HashDoesNotMatchRow)]
    [InlineData(TransferVerifyOutcome.NotFound, ClosureError.HashNotFound)]
    [InlineData(TransferVerifyOutcome.Unavailable, ClosureError.ChainUnavailable)]
    [InlineData(TransferVerifyOutcome.NotSupported, ClosureError.ChainUnavailable)]
    public async Task Approve_UnlessTheServiceConfirmsTheRecipientAndAmount_RefusesAndChangesNothing(
        TransferVerifyOutcome outcome, ClosureError expected)
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final(); // mined, successful, finalized — and still not this row's payment
        _chain.Verify = new TransferVerifyResult(outcome, 123);

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(expected, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        await using var ctx = CreateContext();
        Assert.Equal(OutboundRowClosureStatus.PROPOSED,
            (await ctx.Set<OutboundRowClosure>().AsNoTracking().SingleAsync()).Status);
        Assert.DoesNotContain(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED), _ => true);
    }

    [Fact]
    public async Task Approve_AHashThatDoesNotMatchTheRow_IsLoggedForReviewers()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        _chain.Verify = new TransferVerifyResult(TransferVerifyOutcome.NoMatchingTransfer, 123);

        await ApproveAsync(closure.Id, _bob.Id);

        var refused = Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED));
        Assert.Equal(_bob.Id, refused.ActorId);
        Assert.Contains("HASH_DOES_NOT_MATCH_ROW", refused.NewValue);
    }

    [Fact]
    public async Task Approve_WhenTheRowMovedSinceTheProposal_IsRefused_AndTheNewHashIsNotOverwritten()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        // Between the proposal and the approval the dispatcher sent the row.
        await using (var ctx = CreateContext())
        {
            var tracked = await ctx.Set<BlockchainTransaction>().SingleAsync(b => b.Id == row.Id);
            tracked.TxHash = Hash2;
            tracked.Status = BlockchainTransactionStatus.DETECTED;
            await ctx.SaveChangesAsync();
        }

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.RowNotOpen, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(Hash2, after.TxHash);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, after.Status);
        Assert.Empty(_chain.StatusReads);
    }

    [Fact]
    public async Task Approve_WhenTheHashGotBoundToAnotherRowMeanwhile_IsRefused_NotAnError()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        // After the proposal the dispatcher sent ANOTHER row and its hash is the proposed one.
        await SeedRowAsync(status: BlockchainTransactionStatus.DETECTED, txHash: Hash);

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.HashAlreadyBound, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
    }

    [Fact]
    public async Task Approve_Twice_ChangesTheRowOnce()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();

        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);
        var second = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.ClosureNotOpen, second.Error);
        Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED));
    }

    [Fact]
    public async Task Approve_AnUnknownClosure_Is404()
    {
        var result = await ApproveAsync(Guid.NewGuid(), _bob.Id);

        Assert.Equal(ClosureError.ClosureNotFound, result.Error);
    }

    // =================================================================
    // Reject / withdraw
    // =================================================================

    [Fact]
    public async Task Reject_ClosesTheProposal_AuditsIt_AndLeavesTheRowOpen()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);

        await using (var ctx = CreateContext())
        {
            var result = await Service(ctx).RejectAsync(
                _alice.Id, closure.Id, "Withdrawn: wrong nonce.", null, CancellationToken.None);
            Assert.Null(result.Error);
            Assert.Equal(OutboundRowClosureStatus.REJECTED, result.Value!.Status);
        }

        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        var audit = Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REJECTED));
        Assert.Contains("wrong nonce", audit.NewValue);

        // A rejected proposal cannot be approved afterwards.
        _chain.Status = Final();
        Assert.Equal(ClosureError.ClosureNotOpen, (await ApproveAsync(closure.Id, _bob.Id)).Error);
    }

    [Fact]
    public async Task Reject_NeedsANote()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);

        await using var ctx = CreateContext();
        var result = await Service(ctx).RejectAsync(
            _bob.Id, closure.Id, "  ", null, CancellationToken.None);

        Assert.Equal(ClosureError.Validation, result.Error);
    }

    // =================================================================
    // Candidates and the closed row staying closed
    // =================================================================

    [Fact]
    public async Task Candidates_AreThePendingEthereumOutboundRowsWithoutAHash()
    {
        var open = await SeedRowAsync();
        var withClosure = await SeedRowAsync(type: BlockchainTransactionType.BUYER_REFUND);
        await SeedRowAsync(network: BlockchainNetwork.TRON);
        await SeedRowAsync(status: BlockchainTransactionStatus.DETECTED, txHash: Hash2);
        await SeedRowAsync(type: BlockchainTransactionType.BUYER_PAYMENT, status: BlockchainTransactionStatus.DETECTED);
        await ProposeOkAsync(withClosure.Id);

        await using var ctx = CreateContext();
        var candidates = await Service(ctx).ListCandidatesAsync(CancellationToken.None);

        Assert.Equal(
            new[] { open.Id, withClosure.Id }.OrderBy(i => i),
            candidates.Select(c => c.BlockchainTransactionId).OrderBy(i => i));
        Assert.False(candidates.Single(c => c.BlockchainTransactionId == open.Id).HasLiveClosure);
        Assert.True(candidates.Single(c => c.BlockchainTransactionId == withClosure.Id).HasLiveClosure);
    }

    [Fact]
    public async Task AProposalStaysListed_EvenIfItsProposersAccountWasDeleted()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        await using (var ctx = CreateContext())
        {
            var proposer = await ctx.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == _alice.Id);
            proposer.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        await using var read = CreateContext();
        // Precondition: the account really is hidden from ordinary queries.
        Assert.False(await read.Set<User>().AnyAsync(u => u.Id == _alice.Id));
        var listed = await Service(read).ListAsync(OutboundRowClosureStatus.PROPOSED, CancellationToken.None);

        // A pending money decision must not vanish from the screen with an account.
        Assert.Equal(closure.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task AClosedRow_IsNotSignedAgain_ByTheDispatcher_AndTheConfirmationJobFinishesIt()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);
        _chain.StatusReads.Clear();

        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, transfers: _chain);
        await using (var ctx = CreateContext())
        {
            var dispatch = new OutgoingTransferDispatchJob(
                ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new OneMinuteRetryPolicy(),
                new OutboxService(ctx),
                StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
                new BlockchainNetworkSettingsProvider(ctx), NullOutboundQueueObserver.Instance,
                _clock, NullLogger<OutgoingTransferDispatchJob>.Instance);
            await dispatch.ExecuteAsync();
        }
        Assert.Empty(_chain.Broadcasts); // never signed again — this is the whole point

        await using (var ctx = CreateContext())
        {
            var confirm = new OutgoingTransferConfirmationJob(
                ctx, registry, AlwaysHealthyBlockchainNetworks.Instance,
                new OutboxService(ctx), _clock,
                NullLogger<OutgoingTransferConfirmationJob>.Instance);
            await confirm.ExecuteAsync();
        }
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, after.Status);
        Assert.Equal(Hash, after.TxHash);
    }

    // =================================================================
    // The database repeats the rules
    // =================================================================

    [Fact]
    public async Task Schema_RefusesAnApprovalBySameAdmin_ANonEthereumClosure_AndTwoLiveClosuresForOneHash()
    {
        var row = await SeedRowAsync();
        var other = await SeedRowAsync();

        OutboundRowClosure Make(Guid rowId, string hash, OutboundRowClosureStatus status,
            Guid? decidedBy, string network = "ETHEREUM") => new()
        {
            Id = Guid.NewGuid(),
            BlockchainTransactionId = rowId,
            TxHash = hash,
            Reason = GoodReason,
            Status = status,
            ProposedByAdminId = _alice.Id,
            DecidedByAdminId = decidedBy,
            DecidedAt = decidedBy is null ? null : Start.UtcDateTime,
            CreatedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime,
        };

        // Same admin on both sides.
        await using (var ctx = CreateContext())
        {
            ctx.Set<OutboundRowClosure>().Add(Make(row.Id, Hash, OutboundRowClosureStatus.APPROVED, _alice.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // Network other than Ethereum.
        await using (var ctx = CreateContext())
        {
            var c = Make(row.Id, Hash, OutboundRowClosureStatus.PROPOSED, null);
            c.Network = BlockchainNetwork.TRON;
            ctx.Set<OutboundRowClosure>().Add(c);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // One hash, two live closures.
        await using (var ctx = CreateContext())
        {
            ctx.Set<OutboundRowClosure>().Add(Make(row.Id, Hash, OutboundRowClosureStatus.PROPOSED, null));
            ctx.Set<OutboundRowClosure>().Add(Make(other.Id, Hash, OutboundRowClosureStatus.PROPOSED, null));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        // A different admin is fine.
        await using (var ctx = CreateContext())
        {
            ctx.Set<OutboundRowClosure>().Add(Make(row.Id, Hash, OutboundRowClosureStatus.APPROVED, _bob.Id));
            await ctx.SaveChangesAsync();
        }
    }

    private static TransferStatusResult Final() =>
        new(TransferStatusOutcome.Confirmed, 123, 40, "SUCCESS", null);

    private sealed class ChainStub : IBlockchainTransferClient
    {
        public TransferStatusResult Status { get; set; } =
            new(TransferStatusOutcome.Pending, null, null, null, null);

        public List<Guid> Broadcasts { get; } = [];
        public List<string> StatusReads { get; } = [];

        /// <summary>What the service says about the hash against the row; an exact match unless a test says otherwise.</summary>
        public TransferVerifyResult Verify { get; set; } = new(TransferVerifyOutcome.Match, 123);

        public List<TransferVerifyRequest> VerifyReads { get; } = [];

        public Task<TransferVerifyResult> VerifyTransferAsync(
            TransferVerifyRequest request, CancellationToken cancellationToken)
        {
            VerifyReads.Add(request);
            return Task.FromResult(Verify);
        }

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request, CancellationToken cancellationToken)
        {
            Broadcasts.Add(request.BlockchainTransactionId);
            return Task.FromResult(new TransferBroadcastResult(TransferBroadcastStatus.Success, "0xsent", null, null));
        }

        public Task<TransferStatusResult> GetStatusAsync(string txHash, CancellationToken cancellationToken)
        {
            StatusReads.Add(txHash);
            return Task.FromResult(Status);
        }
    }

    private sealed class OneMinuteRetryPolicy : ITransferRetryPolicy
    {
        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(1));
    }
}
