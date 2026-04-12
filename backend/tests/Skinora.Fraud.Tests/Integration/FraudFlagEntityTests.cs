using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Integration tests for FraudFlag entity (T22).
/// Verifies CRUD, check constraints (scope-based + review-based),
/// FK enforcement, and soft delete against a real SQL Server via TestContainers.
/// </summary>
public class FraudFlagEntityTests : IntegrationTestBase
{
    static FraudFlagEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
    }

    private User _user = null!;
    private User _admin = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "FlaggedUser"
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000099",
            SteamDisplayName = "Admin"
        };
        context.Set<User>().AddRange(_user, _admin);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.FLAGGED,
            SellerId = _user.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000002",
            ItemAssetId = "12345678901",
            ItemClassId = "98765432101",
            ItemName = "AWP | Dragon Lore",
            StablecoinType = StablecoinType.USDT,
            Price = 1200.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 36.000000m,
            TotalAmount = 1236.000000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        context.Set<Transaction>().Add(_transaction);
        await context.SaveChangesAsync();
    }

    private FraudFlag CreateAccountLevelFlag(
        FraudFlagType type = FraudFlagType.MULTI_ACCOUNT,
        ReviewStatus status = ReviewStatus.PENDING)
    {
        return new FraudFlag
        {
            Id = Guid.NewGuid(),
            TransactionId = null,
            UserId = _user.Id,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = type,
            Details = "{\"reason\": \"same wallet across 3 accounts\"}",
            Status = status
        };
    }

    private FraudFlag CreateTransactionPreCreateFlag(
        FraudFlagType type = FraudFlagType.PRICE_DEVIATION,
        ReviewStatus status = ReviewStatus.PENDING)
    {
        return new FraudFlag
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            UserId = _user.Id,
            Scope = FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type = type,
            Details = "{\"market_price\": 800, \"listed_price\": 1200, \"deviation\": 0.50}",
            Status = status
        };
    }

    // ========== FraudFlag CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_AccountLevel_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var flag = CreateAccountLevelFlag();

        // Act
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);

        // Assert
        Assert.Null(loaded.TransactionId);
        Assert.Equal(_user.Id, loaded.UserId);
        Assert.Equal(FraudFlagScope.ACCOUNT_LEVEL, loaded.Scope);
        Assert.Equal(FraudFlagType.MULTI_ACCOUNT, loaded.Type);
        Assert.Contains("same wallet", loaded.Details);
        Assert.Equal(ReviewStatus.PENDING, loaded.Status);
        Assert.Null(loaded.ReviewedByAdminId);
        Assert.Null(loaded.ReviewedAt);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_TransactionPreCreate_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var flag = CreateTransactionPreCreateFlag();

        // Act
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);

        // Assert
        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(_user.Id, loaded.UserId);
        Assert.Equal(FraudFlagScope.TRANSACTION_PRE_CREATE, loaded.Scope);
        Assert.Equal(FraudFlagType.PRICE_DEVIATION, loaded.Type);
        Assert.Contains("market_price", loaded.Details);
        Assert.Equal(ReviewStatus.PENDING, loaded.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_Update_AdminReview_Approved()
    {
        // Arrange
        var flag = CreateTransactionPreCreateFlag();
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();

        // Act — admin approves
        var tracked = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        tracked.Status = ReviewStatus.APPROVED;
        tracked.ReviewedByAdminId = _admin.Id;
        tracked.ReviewedAt = DateTime.UtcNow;
        tracked.AdminNote = "Price deviation within acceptable range after manual check";
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);

        // Assert
        Assert.Equal(ReviewStatus.APPROVED, loaded.Status);
        Assert.Equal(_admin.Id, loaded.ReviewedByAdminId);
        Assert.NotNull(loaded.ReviewedAt);
        Assert.Equal("Price deviation within acceptable range after manual check", loaded.AdminNote);
    }

    // ========== FraudFlag Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var flag = CreateAccountLevelFlag();
        flag.IsDeleted = true;
        flag.DeletedAt = DateTime.UtcNow;
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<FraudFlag>().Where(f => f.Id == flag.Id).ToListAsync();
        var unfiltered = await readCtx.Set<FraudFlag>().IgnoreQueryFilters().Where(f => f.Id == flag.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== FraudFlag Scope-based CHECK Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_AccountLevel_WithTransactionId_Rejected()
    {
        // Arrange — ACCOUNT_LEVEL must have TransactionId NULL
        await using var ctx = CreateContext();
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id, // INVALID for ACCOUNT_LEVEL
            UserId = _user.Id,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = FraudFlagType.MULTI_ACCOUNT,
            Details = "{\"reason\": \"test\"}",
            Status = ReviewStatus.PENDING
        };
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_FraudFlags_AccountLevel_TransactionId", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_PreCreate_WithoutTransactionId_Rejected()
    {
        // Arrange — TRANSACTION_PRE_CREATE must have TransactionId NOT NULL
        await using var ctx = CreateContext();
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            TransactionId = null, // INVALID for TRANSACTION_PRE_CREATE
            UserId = _user.Id,
            Scope = FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type = FraudFlagType.PRICE_DEVIATION,
            Details = "{\"reason\": \"test\"}",
            Status = ReviewStatus.PENDING
        };
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_FraudFlags_PreCreate_TransactionId", ex.InnerException?.Message ?? ex.Message);
    }

    // ========== FraudFlag Review-based CHECK Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_Approved_Without_ReviewedAt_Rejected()
    {
        // Arrange — APPROVED without ReviewedAt/ReviewedByAdminId
        await using var ctx = CreateContext();
        var flag = CreateAccountLevelFlag(status: ReviewStatus.APPROVED);
        // ReviewedAt and ReviewedByAdminId deliberately left null
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_FraudFlags_Approved_ReviewedAt", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_Rejected_Without_ReviewedAt_Rejected()
    {
        // Arrange — REJECTED without ReviewedAt/ReviewedByAdminId
        await using var ctx = CreateContext();
        var flag = CreateAccountLevelFlag(status: ReviewStatus.REJECTED);
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_FraudFlags_Rejected_ReviewedAt", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_Approved_With_ReviewedAt_Accepted()
    {
        // Arrange — APPROVED with ReviewedAt + ReviewedByAdminId
        var flag = CreateTransactionPreCreateFlag(status: ReviewStatus.APPROVED);
        flag.ReviewedAt = DateTime.UtcNow;
        flag.ReviewedByAdminId = _admin.Id;
        flag.AdminNote = "False positive";
        Context.Set<FraudFlag>().Add(flag);

        // Act & Assert — no exception
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.APPROVED, loaded.Status);
        Assert.NotNull(loaded.ReviewedAt);
        Assert.Equal(_admin.Id, loaded.ReviewedByAdminId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_Rejected_With_ReviewedAt_Accepted()
    {
        // Arrange — REJECTED with ReviewedAt + ReviewedByAdminId
        var flag = CreateAccountLevelFlag(status: ReviewStatus.REJECTED);
        flag.ReviewedAt = DateTime.UtcNow;
        flag.ReviewedByAdminId = _admin.Id;
        flag.AdminNote = "Confirmed multi-account abuse";
        Context.Set<FraudFlag>().Add(flag);

        // Act & Assert — no exception
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.REJECTED, loaded.Status);
        Assert.NotNull(loaded.ReviewedAt);
    }

    // ========== FraudFlag FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_InvalidUserId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var flag = CreateAccountLevelFlag();
        flag.UserId = Guid.NewGuid(); // non-existent
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FraudFlag_InvalidTransactionId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var flag = CreateTransactionPreCreateFlag();
        flag.TransactionId = Guid.NewGuid(); // non-existent
        ctx.Set<FraudFlag>().Add(flag);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
