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
    private readonly ChainStub _tronChain = new();
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
            .With(BlockchainNetwork.TRON, transfers: _tronChain)
            .With(BlockchainNetwork.ETHEREUM, transfers: _chain);
        return new OutboundRowClosureService(
            ctx, registry, Wallets(), Contracts, _clock, NullLogger<OutboundRowClosureService>.Instance);
    }

    private static StubPlatformWalletAddressProvider Wallets()
    {
        var wallets = new StubPlatformWalletAddressProvider();
        wallets.Pairs[BlockchainNetwork.ETHEREUM] =
            new PlatformWalletAddresses(BlockchainNetwork.ETHEREUM, HotWallet, EvmLower2);
        return wallets;
    }

    private static readonly IStablecoinContractRegistry Contracts =
        StablecoinContractRegistry.FromOptions(new StablecoinContractOptions());

    private static readonly string UsdtContract =
        Contracts.ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDT);

    private static readonly string UsdcContract =
        Contracts.ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDC);

    private const string HotWallet = "0x1111111111111111111111111111111111111111";
    private const string Buyer = "0x2222222222222222222222222222222222222222";

    // Every seeded row gets its own amount unless a test says otherwise: two
    // rows with the same recipient, token and amount are a look-alike pair the
    // tool refuses (AMBIGUOUS_ROW_MATCH), which only the tests about that rule want.
    private int _seq;

    private async Task<BlockchainTransaction> SeedRowAsync(
        BlockchainNetwork network = BlockchainNetwork.ETHEREUM,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.PENDING,
        BlockchainTransactionType type = BlockchainTransactionType.SELLER_PAYOUT,
        string? txHash = null,
        decimal? amount = null,
        string? toAddress = null,
        string? fromAddress = null,
        string? actualTokenAddress = null)
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
            // A payout never records its sender (the hot wallet signs it); every
            // other outbound row carries what the dispatcher wrote before it signed.
            FromAddress = fromAddress
                ?? (network == BlockchainNetwork.TRON ? TronAddress
                    : type == BlockchainTransactionType.SELLER_PAYOUT ? string.Empty
                    : EvmLower2),
            ToAddress = toAddress ?? (network == BlockchainNetwork.TRON ? SellerWallet : EvmLower),
            Amount = amount ?? 50m + (++_seq),
            Token = StablecoinType.USDT,
            ActualTokenAddress = actualTokenAddress,
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
    [InlineData(TransferStatusOutcome.Unverified, ClosureError.HashDoesNotMatchRow)]
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

        // The status read itself carries what the row moves (T158 (g)).
        Assert.Equal(
            new TransferStatusExpectation(UsdtContract, HotWallet, row.ToAddress, row.Amount),
            Assert.Single(_chain.StatusExpectations));
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
        // The hash really is this payout: from the hot wallet, USDT, to the seller.
        _chain.OnChain = new TransferStatusExpectation(UsdtContract, HotWallet, row.ToAddress, row.Amount);
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);
        _chain.StatusReads.Clear();
        _chain.StatusExpectations.Clear();

        await DispatchAsync();
        Assert.Empty(_chain.Broadcasts); // never signed again — this is the whole point

        await ConfirmAsync();
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, after.Status);
        Assert.Equal(Hash, after.TxHash);
        // The confirmation job's read carried the payout's expectation (T158 (g)).
        var expectation = Assert.Single(_chain.StatusExpectations);
        Assert.Equal(_chain.OnChain, expectation);
    }

    // =================================================================
    // T157 düzeltme turu — what the row moves on chain (⚠ PARA)
    // =================================================================

    [Fact]
    public async Task AWrongTokenRefund_IsVerifiedAgainstTheTokenThatArrived_AndTheConfirmationJobConfirmsIt()
    {
        // The buyer sent USDC to a USDT transaction; the refund returns USDC.
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            toAddress: Buyer,
            actualTokenAddress: UsdcContract);
        _chain.OnChain = new TransferStatusExpectation(UsdcContract, EvmLower2, Buyer, row.Amount);
        _chain.Status = Final();

        await using (var ctx = CreateContext())
        {
            // The candidate list and the approval window show the token that moves.
            var candidate = Assert.Single(await Service(ctx).ListCandidatesAsync(CancellationToken.None));
            Assert.Equal(StablecoinType.USDT, candidate.Token);
            Assert.Equal(StablecoinType.USDC, candidate.ChainToken);
        }
        var closure = await ProposeOkAsync(row.Id);
        Assert.Equal(StablecoinType.USDC, closure.ChainToken);

        var approved = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Null(approved.Error);
        Assert.Equal(StablecoinType.USDC, Assert.Single(_chain.VerifyReads).Token);
        Assert.Equal(UsdcContract, Assert.Single(_chain.StatusExpectations)!.Contract);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, (await ReloadRowAsync(row.Id)).Status);
        var audit = (await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_APPROVED)).Single();
        Assert.Contains("\"chainToken\":\"USDC\"", audit.NewValue);

        _chain.StatusExpectations.Clear();
        await ConfirmAsync();

        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, after.Status);
        Assert.Equal(UsdcContract, Assert.Single(_chain.StatusExpectations)!.Contract);
    }

    [Fact]
    public async Task ARefundAfterTheSweep_IsVerifiedFromTheHotWallet_AndTheConfirmationJobConfirmsIt()
    {
        // T158 (f): the deposit was swept, the dispatcher wrote the hot wallet as sender before it signed.
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.BUYER_REFUND, toAddress: Buyer, fromAddress: HotWallet);
        _chain.OnChain = new TransferStatusExpectation(UsdtContract, HotWallet, Buyer, row.Amount);
        _chain.Status = Final();
        var closure = await ProposeOkAsync(row.Id);

        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);
        Assert.Equal(HotWallet, Assert.Single(_chain.VerifyReads).From, ignoreCase: true);

        _chain.StatusExpectations.Clear();
        await ConfirmAsync();

        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, (await ReloadRowAsync(row.Id)).Status);
        Assert.Equal(HotWallet, Assert.Single(_chain.StatusExpectations)!.FromAddress, ignoreCase: true);
    }

    [Fact]
    public async Task AHashThatMovedTheExpectedTokenInsteadOfTheArrivedOne_DoesNotCloseAWrongTokenRefund()
    {
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            toAddress: Buyer,
            actualTokenAddress: UsdcContract);
        // On chain the hash moved USDT — not what this refund returns.
        _chain.OnChain = new TransferStatusExpectation(UsdtContract, EvmLower2, Buyer, row.Amount);
        _chain.Status = Final();
        var closure = await ProposeOkAsync(row.Id);

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.HashDoesNotMatchRow, result.Error);
        Assert.Equal(BlockchainTransactionStatus.PENDING, (await ReloadRowAsync(row.Id)).Status);
    }

    [Fact]
    public async Task AWrongTokenRefundWhoseTokenIsNotAllowlisted_IsRefused_FailClosed()
    {
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            toAddress: Buyer,
            actualTokenAddress: "0x9999999999999999999999999999999999999999");

        await using var ctx = CreateContext();
        var candidate = Assert.Single(await Service(ctx).ListCandidatesAsync(CancellationToken.None));
        Assert.Null(candidate.ChainToken);
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowTransferUnresolved, result.Error);
        Assert.Equal(0, await ctx.Set<OutboundRowClosure>().CountAsync());
    }

    [Fact]
    public async Task ARefundWithoutARecordedSender_IsRefused_FailClosed()
    {
        // The confirmation job could never confirm it (no expectation → UNKNOWN), so the tool does not close it.
        var row = await SeedRowAsync(type: BlockchainTransactionType.BUYER_REFUND, fromAddress: string.Empty);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowTransferUnresolved, result.Error);
    }

    [Fact]
    public async Task Approve_RefusesARowWhoseSenderWasLostSinceTheProposal_AndLogsIt()
    {
        var row = await SeedRowAsync(type: BlockchainTransactionType.BUYER_REFUND);
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        await using (var ctx = CreateContext())
        {
            var tracked = await ctx.Set<BlockchainTransaction>().SingleAsync(b => b.Id == row.Id);
            tracked.FromAddress = string.Empty;
            await ctx.SaveChangesAsync();
        }

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.RowTransferUnresolved, result.Error);
        Assert.Empty(_chain.StatusReads);
        Assert.Contains("ROW_TRANSFER_UNRESOLVED",
            Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED)).NewValue);
    }

    // =================================================================
    // T157 düzeltme turu — look-alike rows (yönetici kararı, ⚠ PARA)
    // =================================================================

    [Theory]
    [InlineData(BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    public async Task Propose_IsRefused_WhileAnotherOpenRowHasTheSameRecipientTokenAndAmount(
        BlockchainTransactionStatus otherStatus)
    {
        var row = await SeedRowAsync(amount: 75m);
        await SeedRowAsync(status: otherStatus, amount: 75m,
            txHash: otherStatus == BlockchainTransactionStatus.DETECTED ? Hash2 : null);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.AmbiguousRowMatch, result.Error);
        Assert.Equal(0, await ctx.Set<OutboundRowClosure>().CountAsync());
    }

    [Fact]
    public async Task Propose_ALookAlikeOfAnotherKind_StillCounts_ARefundToTheSameAddress()
    {
        var row = await SeedRowAsync(amount: 75m, toAddress: Buyer);
        await SeedRowAsync(type: BlockchainTransactionType.BUYER_REFUND, amount: 75m, toAddress: Buyer.ToUpperInvariant().Replace("0X", "0x"));

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.AmbiguousRowMatch, result.Error);
    }

    [Fact]
    public async Task Propose_IsAllowed_WhenTheOtherRowDiffersOrIsSettled()
    {
        var row = await SeedRowAsync(amount: 75m);
        await SeedRowAsync(amount: 75.000001m);                                                    // another amount
        await SeedRowAsync(amount: 75m, toAddress: Buyer);                                          // another recipient
        await SeedRowAsync(amount: 75m, status: BlockchainTransactionStatus.CONFIRMED, txHash: Hash2); // settled
        await SeedRowAsync(amount: 75m, status: BlockchainTransactionStatus.FAILED);                 // given up
        await SeedRowAsync(network: BlockchainNetwork.TRON, amount: 75m);                           // other network
        await SeedRowAsync(type: BlockchainTransactionType.WRONG_TOKEN_REFUND, amount: 75m,        // another token
            actualTokenAddress: UsdcContract);
        await SeedRowAsync(type: BlockchainTransactionType.BUYER_PAYMENT, amount: 75m,             // inbound
            status: BlockchainTransactionStatus.DETECTED, txHash: "0x" + new string('c', 64));

        await ProposeOkAsync(row.Id);
    }

    [Fact]
    public async Task Approve_IsRefused_WhenALookAlikeRowWasQueuedAfterTheProposal_AndLogsIt()
    {
        var row = await SeedRowAsync(amount: 75m);
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        await SeedRowAsync(amount: 75m);

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.AmbiguousRowMatch, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        Assert.Empty(_chain.StatusReads);
        Assert.Contains("AMBIGUOUS_ROW_MATCH",
            Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED)).NewValue);
    }

    // =================================================================
    // Test gaps closed in the düzeltme turu (B28, B5)
    // =================================================================

    [Fact]
    public async Task Reject_AnApprovedClosure_IsRefused_AndTheApprovalStands()
    {
        var row = await SeedRowAsync();
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);

        await using (var ctx = CreateContext())
        {
            var result = await Service(ctx).RejectAsync(
                _alice.Id, closure.Id, "Changed my mind after the approval.", null, CancellationToken.None);
            Assert.Equal(ClosureError.ClosureNotOpen, result.Error);
        }

        await using var verify = CreateContext();
        var stored = await verify.Set<OutboundRowClosure>().AsNoTracking().SingleAsync();
        Assert.Equal(OutboundRowClosureStatus.APPROVED, stored.Status);
        Assert.Equal(_bob.Id, stored.DecidedByAdminId);
        Assert.Null(stored.DecisionNote);
        Assert.Empty(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REJECTED));
        Assert.Equal(Hash, (await ReloadRowAsync(row.Id)).TxHash);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.FAILED)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    public async Task Propose_ForARowThatIsNotPending_IsRefused_EvenWithoutAHash(BlockchainTransactionStatus status)
    {
        // A FAILED row has no hash: only its status says it is not open.
        var row = await SeedRowAsync(status: status);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowNotOpen, result.Error);
        Assert.Equal(0, await ctx.Set<OutboundRowClosure>().CountAsync());
    }

    // =================================================================
    // T157 kapanışı — the four gaps the re-validation named
    // =================================================================

    [Fact]
    public async Task ARecipientWrittenInMixedCase_MatchesTheSameAccountOnChain_AndTheRowIsClosedAndConfirmed()
    {
        // EIP-55: the row keeps the checksummed spelling the user typed; the
        // Transfer log carries the account as a lowercase topic. Same account.
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.BUYER_REFUND,
            toAddress: EvmChecksummed,
            fromAddress: EvmChecksummed2);
        _chain.OnChain = new TransferStatusExpectation(UsdtContract, EvmLower2, EvmLower, row.Amount);
        _chain.Status = Final();
        var closure = await ProposeOkAsync(row.Id);

        Assert.Null((await ApproveAsync(closure.Id, _bob.Id)).Error);

        var asked = Assert.Single(_chain.VerifyReads);
        Assert.Equal(EvmLower, asked.To, ignoreCase: true);
        Assert.Equal(EvmLower2, asked.From, ignoreCase: true);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, (await ReloadRowAsync(row.Id)).Status);

        _chain.StatusExpectations.Clear();
        await ConfirmAsync();
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, (await ReloadRowAsync(row.Id)).Status);
    }

    [Fact]
    public async Task ALookAlikeWhoseRecipientDiffersOnlyInLetterCase_IsStillALookAlike()
    {
        var row = await SeedRowAsync(amount: 75m, toAddress: EvmChecksummed);
        await SeedRowAsync(amount: 75m, toAddress: EvmLower);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.AmbiguousRowMatch, result.Error);
    }

    [Fact]
    public async Task TheEthereumTool_NeverClosesATronRow_AndClosingAnEthereumRowNeverAsksTheTronService()
    {
        var tron = await SeedRowAsync(network: BlockchainNetwork.TRON);
        var eth = await SeedRowAsync();
        _chain.Status = Final();
        _tronChain.Status = Final();

        // The candidate list is Ethereum only.
        await using (var ctx = CreateContext())
        {
            var candidate = Assert.Single(await Service(ctx).ListCandidatesAsync(CancellationToken.None));
            Assert.Equal(eth.Id, candidate.BlockchainTransactionId);
        }

        // A closure that points at a TRON row (written past the propose step)
        // is still not approved: the row's network is checked again.
        var forged = new OutboundRowClosure
        {
            Id = Guid.NewGuid(),
            BlockchainTransactionId = tron.Id,
            Network = BlockchainNetwork.ETHEREUM,
            TxHash = Hash,
            Reason = GoodReason,
            Status = OutboundRowClosureStatus.PROPOSED,
            ProposedByAdminId = _alice.Id,
            CreatedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime,
        };
        await using (var ctx = CreateContext())
        {
            ctx.Set<OutboundRowClosure>().Add(forged);
            await ctx.SaveChangesAsync();
        }

        var refused = await ApproveAsync(forged.Id, _bob.Id);

        Assert.Equal(ClosureError.RowNotOpen, refused.Error);
        var tronAfter = await ReloadRowAsync(tron.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, tronAfter.Status);
        Assert.Null(tronAfter.TxHash);
        Assert.Empty(_chain.StatusReads);
        Assert.Empty(_chain.VerifyReads);

        // The other way round: closing the Ethereum row asks the Ethereum
        // service only, and leaves the TRON row as it was.
        var closure = await ProposeOkAsync(eth.Id, Hash2);
        Assert.Null((await ApproveAsync(closure.Id, _bob.Id, Hash2)).Error);

        Assert.Single(_chain.VerifyReads);
        Assert.Empty(_tronChain.StatusReads);
        Assert.Empty(_tronChain.VerifyReads);
        Assert.Empty(_tronChain.Broadcasts);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, (await ReloadRowAsync(eth.Id)).Status);
        Assert.Equal(BlockchainTransactionStatus.PENDING, (await ReloadRowAsync(tron.Id)).Status);
    }

    [Theory]
    [InlineData("0x9999999999999999999999999999999999999999")] // not allowlisted
    [InlineData(TronAddress)]                                  // a TRON contract on an Ethereum row
    public async Task Approve_RefusesAWrongTokenRefundWhoseTokenCanNoLongerBeResolved_AndLogsIt(
        string actualTokenAddress)
    {
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            toAddress: Buyer,
            actualTokenAddress: UsdcContract);
        var closure = await ProposeOkAsync(row.Id);
        _chain.Status = Final();
        await using (var ctx = CreateContext())
        {
            var tracked = await ctx.Set<BlockchainTransaction>().SingleAsync(b => b.Id == row.Id);
            tracked.ActualTokenAddress = actualTokenAddress;
            await ctx.SaveChangesAsync();
        }

        var result = await ApproveAsync(closure.Id, _bob.Id);

        Assert.Equal(ClosureError.RowTransferUnresolved, result.Error);
        var after = await ReloadRowAsync(row.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, after.Status);
        Assert.Null(after.TxHash);
        // Never the expected token as a fallback: the chain is not even asked.
        Assert.Empty(_chain.StatusReads);
        Assert.Empty(_chain.VerifyReads);
        Assert.Contains("ROW_TRANSFER_UNRESOLVED",
            Assert.Single(await AuditAsync(AuditAction.OUTBOUND_ROW_CLOSURE_REFUSED)).NewValue);
        await using var verify = CreateContext();
        Assert.Equal(OutboundRowClosureStatus.PROPOSED,
            (await verify.Set<OutboundRowClosure>().AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Propose_RefusesAWrongTokenRefundCarryingATronContract()
    {
        var row = await SeedRowAsync(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            toAddress: Buyer,
            actualTokenAddress: TronAddress);

        await using var ctx = CreateContext();
        var result = await Service(ctx).ProposeAsync(
            _alice.Id, row.Id, Hash, GoodReason, null, CancellationToken.None);

        Assert.Equal(ClosureError.RowTransferUnresolved, result.Error);
        Assert.Equal(0, await ctx.Set<OutboundRowClosure>().CountAsync());
    }

    private async Task DispatchAsync()
    {
        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, transfers: _chain);
        await using var ctx = CreateContext();
        var dispatch = new OutgoingTransferDispatchJob(
            ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new OneMinuteRetryPolicy(),
            new OutboxService(ctx), Contracts,
            new BlockchainNetworkSettingsProvider(ctx), NullOutboundQueueObserver.Instance,
            Wallets(), _clock, NullLogger<OutgoingTransferDispatchJob>.Instance);
        await dispatch.ExecuteAsync();
    }

    /// <summary>The real confirmation job, with the same wallets and contracts as production wiring.</summary>
    private async Task ConfirmAsync()
    {
        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, transfers: _chain);
        await using var ctx = CreateContext();
        var confirm = new OutgoingTransferConfirmationJob(
            ctx, registry, AlwaysHealthyBlockchainNetworks.Instance,
            new OutboxService(ctx), Contracts, Wallets(),
            _clock, NullLogger<OutgoingTransferConfirmationJob>.Instance);
        await confirm.ExecuteAsync();
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
            if (OnChain is not null)
            {
                var asked = new TransferStatusExpectation(
                    Contracts.ResolveContractAddress(BlockchainNetwork.ETHEREUM, request.Token),
                    request.From ?? string.Empty, request.To, request.Amount);
                return Task.FromResult(new TransferVerifyResult(
                    SameTransfer(asked, OnChain) ? TransferVerifyOutcome.Match : TransferVerifyOutcome.NoMatchingTransfer,
                    123));
            }
            return Task.FromResult(Verify);
        }

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request, CancellationToken cancellationToken)
        {
            Broadcasts.Add(request.BlockchainTransactionId);
            return Task.FromResult(new TransferBroadcastResult(TransferBroadcastStatus.Success, "0xsent", null, null));
        }

        /// <summary>What every status read carried (T158 (g)).</summary>
        public List<TransferStatusExpectation?> StatusExpectations { get; } = [];

        /// <summary>
        /// The transfer the hash really carries on chain, like the Ethereum
        /// service sees it for a hash its journal does not hold: when set, a
        /// final status read answers UNKNOWN (<see cref="TransferStatusOutcome.Unverified"/>)
        /// unless the expectation sent with it matches, and the verify read
        /// answers MATCH only for it. Null: answer <see cref="Status"/> / <see cref="Verify"/> as set.
        /// </summary>
        public TransferStatusExpectation? OnChain { get; set; }

        public Task<TransferStatusResult> GetStatusAsync(string txHash, CancellationToken cancellationToken) =>
            GetStatusAsync(txHash, null, cancellationToken);

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, TransferStatusExpectation? expected, CancellationToken cancellationToken)
        {
            StatusReads.Add(txHash);
            StatusExpectations.Add(expected);
            if (OnChain is not null && Status.Outcome == TransferStatusOutcome.Confirmed
                && !SameTransfer(expected, OnChain))
            {
                return Task.FromResult(new TransferStatusResult(
                    TransferStatusOutcome.Unverified, Status.BlockNumber, null, "UNKNOWN", null));
            }
            return Task.FromResult(Status);
        }

        private static bool SameTransfer(TransferStatusExpectation? a, TransferStatusExpectation b) =>
            a is not null
            && string.Equals(a.Contract, b.Contract, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.FromAddress, b.FromAddress, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.ToAddress, b.ToAddress, StringComparison.OrdinalIgnoreCase)
            && a.Amount == b.Amount;
    }

    private sealed class OneMinuteRetryPolicy : ITransferRetryPolicy
    {
        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken) =>
            Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(1));
    }
}
