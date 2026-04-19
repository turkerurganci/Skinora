using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SellerPayoutIssue"/> (T25, 06 §3.8a).
/// Verifies CRUD, state-dependent CHECK constraints, and filtered unique
/// index on (TransactionId) WHERE VerificationStatus != RESOLVED.
/// </summary>
public class SellerPayoutIssueEntityTests : IntegrationTestBase
{
    static SellerPayoutIssueEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private User _seller = null!;
    private User _admin = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000050",
            SteamDisplayName = "Seller-P"
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000051",
            SteamDisplayName = "Admin-P"
        };
        context.Set<User>().AddRange(_seller, _admin);

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.COMPLETED,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000052",
            ItemAssetId = "11111111111",
            ItemClassId = "22222222222",
            ItemName = "Desert Eagle | Blaze",
            StablecoinType = StablecoinType.USDT,
            Price = 100.000000m,
            CommissionRate = 0.02m,
            CommissionAmount = 2.000000m,
            TotalAmount = 102.000000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        context.Set<Transaction>().Add(_transaction);

        await context.SaveChangesAsync();
    }

    private SellerPayoutIssue CreateValid(
        PayoutIssueStatus status = PayoutIssueStatus.REPORTED,
        int retryCount = 0,
        Guid? escalatedToAdminId = null,
        DateTime? resolvedAt = null)
    {
        return new SellerPayoutIssue
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            SellerId = _seller.Id,
            Detail = "Payout did not arrive on chain",
            VerificationStatus = status,
            RetryCount = retryCount,
            EscalatedToAdminId = escalatedToAdminId,
            ResolvedAt = resolvedAt
        };
    }

    // ========== CRUD ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Insert_And_Read_RoundTrips()
    {
        var issue = CreateValid();

        Context.Set<SellerPayoutIssue>().Add(issue);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == issue.Id);

        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(PayoutIssueStatus.REPORTED, loaded.VerificationStatus);
        Assert.Equal(0, loaded.RetryCount);
        Assert.Null(loaded.ResolvedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Update_StatusProgress()
    {
        var issue = CreateValid();
        Context.Set<SellerPayoutIssue>().Add(issue);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == issue.Id);
        tracked.VerificationStatus = PayoutIssueStatus.VERIFYING;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == issue.Id);
        Assert.Equal(PayoutIssueStatus.VERIFYING, loaded.VerificationStatus);
    }

    // ========== State-dependent CHECK ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Escalated_Without_AdminId_Rejected()
    {
        // 06 §3.8a: ESCALATED → EscalatedToAdminId NOT NULL
        var issue = CreateValid(PayoutIssueStatus.ESCALATED);
        Context.Set<SellerPayoutIssue>().Add(issue);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Escalated_With_AdminId_Accepted()
    {
        var issue = CreateValid(PayoutIssueStatus.ESCALATED, escalatedToAdminId: _admin.Id);
        Context.Set<SellerPayoutIssue>().Add(issue);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == issue.Id);
        Assert.Equal(_admin.Id, loaded.EscalatedToAdminId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Resolved_Without_ResolvedAt_Rejected()
    {
        // 06 §3.8a: RESOLVED → ResolvedAt NOT NULL
        var issue = CreateValid(PayoutIssueStatus.RESOLVED);
        Context.Set<SellerPayoutIssue>().Add(issue);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_Resolved_With_ResolvedAt_Accepted()
    {
        var issue = CreateValid(
            PayoutIssueStatus.RESOLVED,
            resolvedAt: DateTime.UtcNow);
        Context.Set<SellerPayoutIssue>().Add(issue);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == issue.Id);
        Assert.NotNull(loaded.ResolvedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_RetryScheduled_Requires_Positive_RetryCount()
    {
        // 06 §3.8a: RETRY_SCHEDULED → RetryCount > 0
        var invalid = CreateValid(PayoutIssueStatus.RETRY_SCHEDULED, retryCount: 0);
        Context.Set<SellerPayoutIssue>().Add(invalid);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_RetryScheduled_With_PositiveCount_Accepted()
    {
        var valid = CreateValid(PayoutIssueStatus.RETRY_SCHEDULED, retryCount: 1);
        Context.Set<SellerPayoutIssue>().Add(valid);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SellerPayoutIssue>().FirstAsync(i => i.Id == valid.Id);
        Assert.Equal(1, loaded.RetryCount);
    }

    // ========== Filtered Unique ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_TwoActive_SameTransaction_Rejected()
    {
        // 06 §3.8a: UNIQUE(TransactionId) WHERE VerificationStatus != RESOLVED
        var first = CreateValid();
        Context.Set<SellerPayoutIssue>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid();
        ctx.Set<SellerPayoutIssue>().Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SellerPayoutIssue_ReopenAfter_Resolved_Allowed()
    {
        // Once the previous issue is RESOLVED the filter excludes it, so a
        // brand-new issue for the same transaction is permitted.
        var first = CreateValid(PayoutIssueStatus.RESOLVED, resolvedAt: DateTime.UtcNow);
        Context.Set<SellerPayoutIssue>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var next = CreateValid();
        ctx.Set<SellerPayoutIssue>().Add(next);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var count = await readCtx.Set<SellerPayoutIssue>()
            .Where(i => i.TransactionId == _transaction.Id)
            .CountAsync();
        Assert.Equal(2, count);
    }
}
