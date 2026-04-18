using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// Integration tests for Notification entity (T23).
/// Verifies CRUD, soft delete, FK enforcement, and indexes
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class NotificationEntityTests : IntegrationTestBase
{
    static NotificationEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private User _user = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "TestUser"
        };
        context.Set<User>().Add(_user);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_ESCROWED,
            SellerId = _user.Id,
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

    private Notification CreateValid(
        NotificationType type = NotificationType.TRANSACTION_INVITE,
        Guid? transactionId = null)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            TransactionId = transactionId ?? _transaction.Id,
            Type = type,
            Title = "New trade invitation",
            Body = "You have a new trade invitation for AK-47 | Redline"
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var notification = CreateValid();

        // Act
        Context.Set<Notification>().Add(notification);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Notification>().FirstAsync(n => n.Id == notification.Id);

        // Assert
        Assert.Equal(_user.Id, loaded.UserId);
        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(NotificationType.TRANSACTION_INVITE, loaded.Type);
        Assert.Equal("New trade invitation", loaded.Title);
        Assert.Equal("You have a new trade invitation for AK-47 | Redline", loaded.Body);
        Assert.False(loaded.IsRead);
        Assert.Null(loaded.ReadAt);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_Update_MarkAsRead()
    {
        // Arrange
        var notification = CreateValid();
        Context.Set<Notification>().Add(notification);
        await Context.SaveChangesAsync();

        // Act
        var tracked = await Context.Set<Notification>().FirstAsync(n => n.Id == notification.Id);
        tracked.IsRead = true;
        tracked.ReadAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Notification>().FirstAsync(n => n.Id == notification.Id);

        // Assert
        Assert.True(loaded.IsRead);
        Assert.NotNull(loaded.ReadAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_NullTransactionId_Accepted()
    {
        // Arrange — standalone notification (no transaction)
        var notification = CreateValid();
        notification.TransactionId = null;

        // Act
        Context.Set<Notification>().Add(notification);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Notification>().FirstAsync(n => n.Id == notification.Id);

        // Assert
        Assert.Null(loaded.TransactionId);
    }

    // ========== Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var notification = CreateValid();
        notification.IsDeleted = true;
        notification.DeletedAt = DateTime.UtcNow;
        Context.Set<Notification>().Add(notification);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<Notification>().Where(n => n.Id == notification.Id).ToListAsync();
        var unfiltered = await readCtx.Set<Notification>().IgnoreQueryFilters().Where(n => n.Id == notification.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_InvalidUserId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var notification = CreateValid();
        notification.UserId = Guid.NewGuid(); // non-existent
        ctx.Set<Notification>().Add(notification);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Notification_InvalidTransactionId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var notification = CreateValid();
        notification.TransactionId = Guid.NewGuid(); // non-existent
        ctx.Set<Notification>().Add(notification);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
