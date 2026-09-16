using Skinora.Fraud.Application.Account;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="AccountFlagChecker"/> — the
/// <c>IAccountFlagChecker</c> port impl wired by T45's transactions module
/// (07 §7.3 eligibility surface, 02 §14.0 account flag).
/// </summary>
public class AccountFlagCheckerTests : IntegrationTestBase
{
    static AccountFlagCheckerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
    }

    private User _user = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000080",
            SteamDisplayName = "FlaggedUser"
        };
        context.Set<User>().Add(_user);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_False_When_No_Flag()
    {
        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasActiveAccountFlagAsync(_user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_True_When_Pending_Account_Flag_Exists()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.PENDING);

        var sut = new AccountFlagChecker(Context);
        Assert.True(await sut.HasActiveAccountFlagAsync(_user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_True_When_Approved_Account_Flag_Exists()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.APPROVED);

        var sut = new AccountFlagChecker(Context);
        Assert.True(await sut.HasActiveAccountFlagAsync(_user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_False_When_Only_Rejected_Flag_Exists()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.REJECTED);

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasActiveAccountFlagAsync(_user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Ignores_Soft_Deleted_Flags()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.PENDING, isDeleted: true);

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasActiveAccountFlagAsync(_user.Id, CancellationToken.None));
    }

    // ---- HasPendingAccountFlagAsync (02 §14.2 non-delivery sanction idempotency) ----

    [Fact]
    public async Task Pending_Returns_True_For_A_Saved_Pending_Flag_Of_That_Type()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.PENDING, type: FraudFlagType.ABNORMAL_BEHAVIOR);

        var sut = new AccountFlagChecker(Context);
        Assert.True(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_Returns_True_For_A_Staged_But_Unsaved_Flag()
    {
        // The deadline scanner evaluates a whole flushed batch in one unit of
        // work; the second evaluation for the same seller must see the flag the
        // first one staged, or the admin queue gets two flags for one pattern.
        Context.Set<FraudFlag>().Add(new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = FraudFlagType.ABNORMAL_BEHAVIOR,
            Status = ReviewStatus.PENDING,
            Details = "{}",
        });

        var sut = new AccountFlagChecker(Context);
        Assert.True(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_Returns_False_For_A_Pending_Flag_Of_Another_Type()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.PENDING, type: FraudFlagType.MULTI_ACCOUNT);

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    [Theory]
    [InlineData(ReviewStatus.APPROVED)]
    [InlineData(ReviewStatus.REJECTED)]
    public async Task Pending_Returns_False_Once_An_Admin_Has_Reviewed_The_Flag(ReviewStatus status)
    {
        // A reviewed flag is a closed decision: a NEW repeat deserves a new review.
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, status, type: FraudFlagType.ABNORMAL_BEHAVIOR);

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pending_Ignores_A_Transaction_Level_Flag_Of_That_Type(bool saved)
    {
        // ABNORMAL_BEHAVIOR has a transaction-level producer too — the dormant
        // account pre-create rule (02 §14.4). A seller whose flagged transaction
        // awaits review has no ACCOUNT flag in front of an admin, so that flag
        // must not suppress the non-delivery account flag, or the 02 §14.0
        // account block is never applied. Neither half of the scope filter was
        // pinned (validation finding): saved-and-detached exercises the query,
        // unsaved exercises the change-tracker look-up the scanner relies on.
        var transactionId = await InsertFlaggedTransactionAsync();
        Context.Set<FraudFlag>().Add(new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            TransactionId = transactionId,
            Scope = FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type = FraudFlagType.ABNORMAL_BEHAVIOR,
            Status = ReviewStatus.PENDING,
            Details = "{}",
        });
        if (saved)
        {
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    [Fact]
    public async Task Pending_Ignores_Soft_Deleted_Flags()
    {
        await InsertFlagAsync(FraudFlagScope.ACCOUNT_LEVEL, ReviewStatus.PENDING, isDeleted: true, type: FraudFlagType.ABNORMAL_BEHAVIOR);

        var sut = new AccountFlagChecker(Context);
        Assert.False(await sut.HasPendingAccountFlagAsync(_user.Id, FraudFlagType.ABNORMAL_BEHAVIOR, CancellationToken.None));
    }

    private async Task InsertFlagAsync(
        FraudFlagScope scope,
        ReviewStatus status,
        bool isDeleted = false,
        FraudFlagType type = FraudFlagType.MULTI_ACCOUNT)
    {
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Scope = scope,
            Type = type,
            Status = status,
            Details = "{}",
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
        };

        if (status is ReviewStatus.APPROVED or ReviewStatus.REJECTED)
        {
            flag.ReviewedAt = DateTime.UtcNow;
            flag.ReviewedByAdminId = _user.Id; // self-FK acceptable in tests
        }

        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();
    }

    /// <summary>A FLAGGED (pre-create) transaction for <c>CK_FraudFlags_PreCreate_TransactionId</c>.</summary>
    private async Task<Guid> InsertFlaggedTransactionAsync()
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.FLAGGED,
            SellerId = _user.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000081",
            ItemAssetId = "flagcheck01",
            ItemClassId = "1",
            ItemName = "Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 5000m,
            CommissionRate = 0.02m,
            CommissionAmount = 100m,
            TotalAmount = 5100m,
            SellerPayoutAddress = "TXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            PaymentTimeoutMinutes = 60,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx.Id;
    }
}
