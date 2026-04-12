using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Disputes.Domain.Entities;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Disputes.Tests.Integration;

/// <summary>
/// Integration tests for Dispute entity (T22).
/// Verifies CRUD, check constraints, unique constraints, FK enforcement,
/// and soft delete against a real SQL Server instance via TestContainers.
/// </summary>
public class DisputeEntityTests : IntegrationTestBase
{
    static DisputeEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
    }

    private User _buyer = null!;
    private User _admin = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Buyer"
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000099",
            SteamDisplayName = "Admin"
        };
        context.Set<User>().AddRange(_buyer, _admin);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_ESCROWED,
            SellerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000002",
            ItemAssetId = "12345678901",
            ItemClassId = "98765432101",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 1.500000m,
            TotalAmount = 51.500000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        context.Set<Transaction>().Add(_transaction);
        await context.SaveChangesAsync();
    }

    private Dispute CreateValidDispute(
        DisputeType type = DisputeType.PAYMENT,
        DisputeStatus status = DisputeStatus.OPEN)
    {
        return new Dispute
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            OpenedByUserId = _buyer.Id,
            Type = type,
            Status = status
        };
    }

    // ========== Dispute CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var dispute = CreateValidDispute();
        dispute.SystemCheckResult = "{\"blockchain_check\": \"no_payment_found\"}";
        dispute.UserDescription = "I paid but system does not see it";

        // Act
        Context.Set<Dispute>().Add(dispute);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);

        // Assert
        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(_buyer.Id, loaded.OpenedByUserId);
        Assert.Equal(DisputeType.PAYMENT, loaded.Type);
        Assert.Equal(DisputeStatus.OPEN, loaded.Status);
        Assert.Equal("{\"blockchain_check\": \"no_payment_found\"}", loaded.SystemCheckResult);
        Assert.Equal("I paid but system does not see it", loaded.UserDescription);
        Assert.Null(loaded.AdminId);
        Assert.Null(loaded.AdminNote);
        Assert.Null(loaded.ResolvedAt);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_Update_Escalation()
    {
        // Arrange
        var dispute = CreateValidDispute();
        Context.Set<Dispute>().Add(dispute);
        await Context.SaveChangesAsync();

        // Act — escalate
        var tracked = await Context.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);
        tracked.Status = DisputeStatus.ESCALATED;
        tracked.UserDescription = "Need admin help";
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);

        // Assert
        Assert.Equal(DisputeStatus.ESCALATED, loaded.Status);
        Assert.Equal("Need admin help", loaded.UserDescription);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_Close_With_ResolvedAt()
    {
        // Arrange
        var dispute = CreateValidDispute();
        Context.Set<Dispute>().Add(dispute);
        await Context.SaveChangesAsync();

        // Act — close
        var tracked = await Context.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);
        tracked.Status = DisputeStatus.CLOSED;
        tracked.ResolvedAt = DateTime.UtcNow;
        tracked.AdminId = _admin.Id;
        tracked.AdminNote = "Payment confirmed manually";
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);

        // Assert
        Assert.Equal(DisputeStatus.CLOSED, loaded.Status);
        Assert.NotNull(loaded.ResolvedAt);
        Assert.Equal(_admin.Id, loaded.AdminId);
        Assert.Equal("Payment confirmed manually", loaded.AdminNote);
    }

    // ========== Dispute Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var dispute = CreateValidDispute();
        dispute.IsDeleted = true;
        dispute.DeletedAt = DateTime.UtcNow;
        Context.Set<Dispute>().Add(dispute);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<Dispute>().Where(d => d.Id == dispute.Id).ToListAsync();
        var unfiltered = await readCtx.Set<Dispute>().IgnoreQueryFilters().Where(d => d.Id == dispute.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== Dispute Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_TransactionId_Type_Unique_Rejects_Duplicate()
    {
        // Arrange — first PAYMENT dispute
        var dispute1 = CreateValidDispute(DisputeType.PAYMENT);
        Context.Set<Dispute>().Add(dispute1);
        await Context.SaveChangesAsync();

        // Act — second PAYMENT dispute for same transaction
        await using var ctx2 = CreateContext();
        var dispute2 = CreateValidDispute(DisputeType.PAYMENT);
        ctx2.Set<Dispute>().Add(dispute2);

        // Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UQ_Disputes_TransactionId_Type", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_TransactionId_Type_Unique_Allows_DifferentTypes()
    {
        // Arrange — PAYMENT dispute
        var dispute1 = CreateValidDispute(DisputeType.PAYMENT);
        Context.Set<Dispute>().Add(dispute1);
        await Context.SaveChangesAsync();

        // Act — DELIVERY dispute for same transaction (different type)
        var dispute2 = CreateValidDispute(DisputeType.DELIVERY);
        Context.Set<Dispute>().Add(dispute2);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<Dispute>()
            .Where(d => d.TransactionId == _transaction.Id)
            .CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_TransactionId_Type_Unique_Blocks_Even_SoftDeleted()
    {
        // Arrange — soft-deleted PAYMENT dispute
        var dispute1 = CreateValidDispute(DisputeType.PAYMENT);
        dispute1.IsDeleted = true;
        dispute1.DeletedAt = DateTime.UtcNow;
        Context.Set<Dispute>().Add(dispute1);
        await Context.SaveChangesAsync();

        // Act — new PAYMENT dispute for same transaction (unfiltered unique blocks this)
        await using var ctx2 = CreateContext();
        var dispute2 = CreateValidDispute(DisputeType.PAYMENT);
        ctx2.Set<Dispute>().Add(dispute2);

        // Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UQ_Disputes_TransactionId_Type", ex.InnerException?.Message ?? ex.Message);
    }

    // ========== Dispute CHECK Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_Closed_Without_ResolvedAt_Rejected()
    {
        // Arrange — CLOSED without ResolvedAt
        await using var ctx = CreateContext();
        var dispute = CreateValidDispute(DisputeType.WRONG_ITEM, DisputeStatus.CLOSED);
        // ResolvedAt deliberately left null
        ctx.Set<Dispute>().Add(dispute);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_Disputes_Closed_ResolvedAt", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_Closed_With_ResolvedAt_Accepted()
    {
        // Arrange — CLOSED with ResolvedAt
        var dispute = CreateValidDispute(DisputeType.DELIVERY, DisputeStatus.CLOSED);
        dispute.ResolvedAt = DateTime.UtcNow;
        dispute.AdminId = _admin.Id;
        dispute.AdminNote = "Resolved";
        Context.Set<Dispute>().Add(dispute);

        // Act & Assert — no exception
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Dispute>().FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.CLOSED, loaded.Status);
        Assert.NotNull(loaded.ResolvedAt);
    }

    // ========== Dispute FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_InvalidTransactionId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var dispute = CreateValidDispute();
        dispute.TransactionId = Guid.NewGuid(); // non-existent
        ctx.Set<Dispute>().Add(dispute);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispute_InvalidOpenedByUserId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var dispute = CreateValidDispute();
        dispute.OpenedByUserId = Guid.NewGuid(); // non-existent
        ctx.Set<Dispute>().Add(dispute);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
