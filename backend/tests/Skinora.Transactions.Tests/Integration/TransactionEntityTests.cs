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
/// Integration tests for Transaction and TransactionHistory entities.
/// Verifies CRUD, check constraints, indexes, and RowVersion concurrency
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class TransactionEntityTests : IntegrationTestBase
{
    // Sentinel admin GUID per 06 §8.9
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    static TransactionEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Seller"
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000002",
            SteamDisplayName = "Buyer"
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000003",
            SteamDisplayName = "Admin"
        };

        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();
    }

    private Transaction CreateValidTransaction(
        TransactionStatus status = TransactionStatus.CREATED,
        BuyerIdentificationMethod method = BuyerIdentificationMethod.STEAM_ID)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = method,
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

        if (method == BuyerIdentificationMethod.STEAM_ID)
            tx.TargetBuyerSteamId = "76561198000000002";
        else
            tx.InviteToken = "abc123def456";

        return tx;
    }

    // ========== CRUD Tests ==========

    [Fact]
    public async Task Transaction_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var tx = CreateValidTransaction();

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);

        // Assert
        Assert.Equal(TransactionStatus.CREATED, loaded.Status);
        Assert.Equal(_seller.Id, loaded.SellerId);
        Assert.Equal("AK-47 | Redline", loaded.ItemName);
        Assert.Equal(50.000000m, loaded.Price);
        Assert.Equal(0.0300m, loaded.CommissionRate);
        Assert.Equal(1.500000m, loaded.CommissionAmount);
        Assert.Equal(51.500000m, loaded.TotalAmount);
    }

    [Fact]
    public async Task Transaction_Update_ChangesStatus()
    {
        // Arrange
        var tx = CreateValidTransaction();
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Act
        tx.Status = TransactionStatus.ACCEPTED;
        tx.BuyerId = _buyer.Id;
        tx.BuyerRefundAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";
        tx.AcceptedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);

        // Assert
        Assert.Equal(TransactionStatus.ACCEPTED, loaded.Status);
        Assert.Equal(_buyer.Id, loaded.BuyerId);
    }

    [Fact]
    public async Task Transaction_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var tx = CreateValidTransaction();
        tx.IsDeleted = true;
        tx.DeletedAt = DateTime.UtcNow;

        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Act — default query (soft delete filter)
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<Transaction>().Where(t => t.Id == tx.Id).ToListAsync();
        var unfiltered = await readCtx.Set<Transaction>().IgnoreQueryFilters().Where(t => t.Id == tx.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== TransactionHistory Tests ==========

    [Fact]
    public async Task TransactionHistory_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var tx = CreateValidTransaction();
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        var history = new TransactionHistory
        {
            TransactionId = tx.Id,
            PreviousStatus = null,
            NewStatus = TransactionStatus.CREATED,
            Trigger = "SellerCreated",
            ActorType = ActorType.USER,
            ActorId = _seller.Id,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        Context.Set<TransactionHistory>().Add(history);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TransactionHistory>().FirstAsync(h => h.TransactionId == tx.Id);

        // Assert
        Assert.Equal(TransactionStatus.CREATED, loaded.NewStatus);
        Assert.Null(loaded.PreviousStatus);
        Assert.Equal("SellerCreated", loaded.Trigger);
        Assert.True(loaded.Id > 0); // IDENTITY generated
    }

    [Fact]
    public async Task TransactionHistory_Navigation_LoadsFromTransaction()
    {
        // Arrange
        var tx = CreateValidTransaction();
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        Context.Set<TransactionHistory>().AddRange(
            new TransactionHistory
            {
                TransactionId = tx.Id,
                NewStatus = TransactionStatus.CREATED,
                Trigger = "SellerCreated",
                ActorType = ActorType.USER,
                ActorId = _seller.Id,
                CreatedAt = DateTime.UtcNow
            },
            new TransactionHistory
            {
                TransactionId = tx.Id,
                PreviousStatus = TransactionStatus.CREATED,
                NewStatus = TransactionStatus.ACCEPTED,
                Trigger = "BuyerAccepted",
                ActorType = ActorType.USER,
                ActorId = _buyer.Id,
                CreatedAt = DateTime.UtcNow
            });
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>()
            .Include(t => t.History)
            .FirstAsync(t => t.Id == tx.Id);

        // Assert
        Assert.Equal(2, loaded.History.Count);
    }

    // ========== Check Constraint Tests ==========

    [Fact]
    public async Task CK_Cancel_Violated_WhenCancelFieldsMissing()
    {
        // Arrange — cancelled status without required cancel fields
        var tx = CreateValidTransaction(TransactionStatus.CANCELLED_SELLER);
        // CancelledBy, CancelReason, CancelledAt are all null → constraint violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Cancel_Satisfied_WhenCancelFieldsPresent()
    {
        // Arrange — cancelled status with required cancel fields
        var tx = CreateValidTransaction(TransactionStatus.CANCELLED_SELLER);
        tx.CancelledBy = CancelledByType.SELLER;
        tx.CancelReason = "Seller changed mind";
        tx.CancelledAt = DateTime.UtcNow;

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, loaded.Status);
    }

    [Fact]
    public async Task CK_Hold_Violated_WhenHoldFieldsMissing()
    {
        // Arrange — IsOnHold = true without required hold fields
        var tx = CreateValidTransaction();
        tx.IsOnHold = true;
        // Also need freeze fields for CK_Transactions_FreezeHold_Reverse
        tx.TimeoutFrozenAt = DateTime.UtcNow;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutRemainingSeconds = 300;
        // EmergencyHoldAt, EmergencyHoldReason, EmergencyHoldByAdminId are null → CK_Hold violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Hold_Satisfied_WhenAllHoldFieldsPresent()
    {
        // Arrange — valid emergency hold state
        var tx = CreateValidTransaction();
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = DateTime.UtcNow;
        tx.EmergencyHoldReason = "Suspicious activity";
        tx.EmergencyHoldByAdminId = _admin.Id;
        tx.TimeoutFrozenAt = DateTime.UtcNow;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutRemainingSeconds = 300;

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.True(loaded.IsOnHold);
    }

    [Fact]
    public async Task CK_FreezeActive_Violated_WhenFreezeWithoutReason()
    {
        // Arrange — TimeoutFrozenAt set but TimeoutFreezeReason is null
        var tx = CreateValidTransaction();
        tx.TimeoutFrozenAt = DateTime.UtcNow;
        // TimeoutFreezeReason = null, TimeoutRemainingSeconds = null → CK_FreezeActive violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_FreezePassive_Violated_WhenReasonWithoutFreeze()
    {
        // Arrange — TimeoutFrozenAt is null but TimeoutFreezeReason set
        var tx = CreateValidTransaction();
        tx.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        tx.TimeoutRemainingSeconds = 100;
        // TimeoutFrozenAt = null → CK_FreezePassive violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_FreezeHold_Forward_Violated_WhenEmergencyHoldFreezeWithoutHold()
    {
        // Arrange — freeze reason is EMERGENCY_HOLD but IsOnHold = false
        var tx = CreateValidTransaction();
        tx.TimeoutFrozenAt = DateTime.UtcNow;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutRemainingSeconds = 300;
        // IsOnHold = false → CK_FreezeHold_Forward violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_FreezeHold_Reverse_Violated_WhenHoldWithoutEmergencyFreeze()
    {
        // Arrange — IsOnHold = true but no EMERGENCY_HOLD freeze
        var tx = CreateValidTransaction();
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = DateTime.UtcNow;
        tx.EmergencyHoldReason = "Test";
        tx.EmergencyHoldByAdminId = _admin.Id;
        // TimeoutFrozenAt = null → CK_FreezeHold_Reverse violation

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_BuyerMethod_SteamId_Violated_WhenMissing()
    {
        // Arrange — STEAM_ID method without TargetBuyerSteamId
        var tx = CreateValidTransaction(method: BuyerIdentificationMethod.STEAM_ID);
        tx.TargetBuyerSteamId = null; // violates constraint

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_BuyerMethod_OpenLink_Satisfied()
    {
        // Arrange
        var tx = CreateValidTransaction(method: BuyerIdentificationMethod.OPEN_LINK);

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(BuyerIdentificationMethod.OPEN_LINK, loaded.BuyerIdentificationMethod);
        Assert.NotNull(loaded.InviteToken);
        Assert.Null(loaded.TargetBuyerSteamId);
    }

    // ========== Unique Index Tests ==========

    [Fact]
    public async Task InviteToken_Unique_PreventsDuplicates()
    {
        // Arrange
        var tx1 = CreateValidTransaction(method: BuyerIdentificationMethod.OPEN_LINK);
        tx1.InviteToken = "unique-token-123";
        Context.Set<Transaction>().Add(tx1);
        await Context.SaveChangesAsync();

        // Act — second transaction with same token
        await using var ctx2 = CreateContext();
        var tx2 = CreateValidTransaction(method: BuyerIdentificationMethod.OPEN_LINK);
        tx2.InviteToken = "unique-token-123";
        ctx2.Set<Transaction>().Add(tx2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task InviteToken_Null_AllowsMultiple()
    {
        // Arrange — filtered unique index: NULL tokens are not constrained
        var tx1 = CreateValidTransaction(method: BuyerIdentificationMethod.STEAM_ID);
        var tx2 = CreateValidTransaction(method: BuyerIdentificationMethod.STEAM_ID);
        // Both have InviteToken = null

        // Act
        Context.Set<Transaction>().AddRange(tx1, tx2);
        await Context.SaveChangesAsync();

        // Assert — no exception
        Assert.NotEqual(tx1.Id, tx2.Id);
    }

    // ========== RowVersion Concurrency Tests ==========

    [Fact]
    public async Task RowVersion_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        // Arrange
        var tx = CreateValidTransaction();
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Act — load same entity in two contexts
        await using var ctx1 = CreateContext();
        await using var ctx2 = CreateContext();

        var tx1 = await ctx1.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        var tx2 = await ctx2.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);

        // First update succeeds
        tx1.Status = TransactionStatus.ACCEPTED;
        tx1.BuyerId = _buyer.Id;
        tx1.AcceptedAt = DateTime.UtcNow;
        await ctx1.SaveChangesAsync();

        // Second update on stale RowVersion should fail
        tx2.Status = TransactionStatus.CANCELLED_SELLER;
        tx2.CancelledBy = CancelledByType.SELLER;
        tx2.CancelReason = "Changed mind";
        tx2.CancelledAt = DateTime.UtcNow;

        // Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());
    }

    // ========== Commission Calculation Verification ==========

    [Fact]
    public async Task CommissionFields_StoredWithCorrectPrecision()
    {
        // Arrange — §8.3: CommissionAmount = ROUND(Price × CommissionRate, 6, ToZero)
        var tx = CreateValidTransaction();
        tx.Price = 123.456789m;
        tx.CommissionRate = 0.0350m;
        // CommissionAmount = ROUND(123.456789 * 0.0350, 6, ToZero) = ROUND(4.320987615, 6) = 4.320987
        tx.CommissionAmount = Math.Round(tx.Price * tx.CommissionRate, 6, MidpointRounding.ToZero);
        tx.TotalAmount = tx.Price + tx.CommissionAmount;

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);

        // Assert
        Assert.Equal(123.456789m, loaded.Price);
        Assert.Equal(0.0350m, loaded.CommissionRate);
        Assert.Equal(4.320987m, loaded.CommissionAmount);
        Assert.Equal(tx.Price + tx.CommissionAmount, loaded.TotalAmount);
    }

    // ========== FK Relationship Tests ==========

    [Fact]
    public async Task Transaction_FK_Seller_EnforcedByDatabase()
    {
        // Arrange — non-existent seller
        var tx = CreateValidTransaction();
        tx.SellerId = Guid.NewGuid(); // doesn't exist

        Context.Set<Transaction>().Add(tx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task TransactionHistory_FK_Transaction_EnforcedByDatabase()
    {
        // Arrange — non-existent transaction
        var history = new TransactionHistory
        {
            TransactionId = Guid.NewGuid(), // doesn't exist
            NewStatus = TransactionStatus.CREATED,
            Trigger = "SellerCreated",
            ActorType = ActorType.USER,
            ActorId = _seller.Id,
            CreatedAt = DateTime.UtcNow
        };

        Context.Set<TransactionHistory>().Add(history);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task TransactionHistory_FK_Actor_EnforcedByDatabase()
    {
        // Arrange
        var tx = CreateValidTransaction();
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        var history = new TransactionHistory
        {
            TransactionId = tx.Id,
            NewStatus = TransactionStatus.CREATED,
            Trigger = "SellerCreated",
            ActorType = ActorType.USER,
            ActorId = Guid.NewGuid(), // doesn't exist
            CreatedAt = DateTime.UtcNow
        };

        Context.Set<TransactionHistory>().Add(history);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    // ========== Non-Freeze Valid State Tests ==========

    [Fact]
    public async Task ValidFreeze_Maintenance_InsertSucceeds()
    {
        // Arrange — valid freeze without hold (non-EMERGENCY_HOLD reason)
        var tx = CreateValidTransaction();
        tx.TimeoutFrozenAt = DateTime.UtcNow;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        tx.TimeoutRemainingSeconds = 600;

        // Act
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TimeoutFreezeReason.MAINTENANCE, loaded.TimeoutFreezeReason);
        Assert.False(loaded.IsOnHold);
    }
}
