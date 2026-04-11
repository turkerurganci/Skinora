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
/// Integration tests for PaymentAddress and BlockchainTransaction entities.
/// Verifies CRUD, check constraints, unique constraints, and FK enforcement
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class PaymentBlockchainEntityTests : IntegrationTestBase
{
    static PaymentBlockchainEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private User _seller = null!;
    private Transaction _transaction = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "Seller"
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = _seller.Id,
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

    private PaymentAddress CreateValidPaymentAddress(int hdIndex = 0)
    {
        return new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            Address = $"T{Guid.NewGuid():N}{hdIndex:D2}",
            HdWalletIndex = hdIndex,
            ExpectedAmount = 51.500000m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE
        };
    }

    private BlockchainTransaction CreateValidBlockchainTx(
        BlockchainTransactionType type = BlockchainTransactionType.BUYER_PAYMENT,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.DETECTED,
        Guid? paymentAddressId = null)
    {
        return new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            PaymentAddressId = paymentAddressId,
            Type = type,
            FromAddress = "TSenderAddress1234567890123456789",
            ToAddress = "TReceiverAddress123456789012345678",
            Amount = 51.500000m,
            Token = StablecoinType.USDT,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ========== PaymentAddress CRUD Tests ==========

    [Fact]
    public async Task PaymentAddress_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var pa = CreateValidPaymentAddress();

        // Act
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<PaymentAddress>().FirstAsync(p => p.Id == pa.Id);

        // Assert
        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(51.500000m, loaded.ExpectedAmount);
        Assert.Equal(StablecoinType.USDT, loaded.ExpectedToken);
        Assert.Equal(MonitoringStatus.ACTIVE, loaded.MonitoringStatus);
    }

    [Fact]
    public async Task PaymentAddress_Update_ChangesMonitoringStatus()
    {
        // Arrange
        var pa = CreateValidPaymentAddress();
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        // Act
        pa.MonitoringStatus = MonitoringStatus.STOPPED;
        pa.MonitoringExpiresAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<PaymentAddress>().FirstAsync(p => p.Id == pa.Id);

        // Assert
        Assert.Equal(MonitoringStatus.STOPPED, loaded.MonitoringStatus);
        Assert.NotNull(loaded.MonitoringExpiresAt);
    }

    [Fact]
    public async Task PaymentAddress_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var pa = CreateValidPaymentAddress();
        pa.IsDeleted = true;
        pa.DeletedAt = DateTime.UtcNow;

        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<PaymentAddress>().Where(p => p.Id == pa.Id).ToListAsync();
        var unfiltered = await readCtx.Set<PaymentAddress>().IgnoreQueryFilters().Where(p => p.Id == pa.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== PaymentAddress Unique Constraint Tests ==========

    [Fact]
    public async Task PaymentAddress_TransactionId_Unique()
    {
        // Arrange — first PA for transaction
        var pa1 = CreateValidPaymentAddress(hdIndex: 1);
        Context.Set<PaymentAddress>().Add(pa1);
        await Context.SaveChangesAsync();

        // Act — second PA for same transaction
        await using var ctx2 = CreateContext();
        var pa2 = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id, // same transaction → 1:1 violation
            Address = $"T{Guid.NewGuid():N}02",
            HdWalletIndex = 2,
            ExpectedAmount = 51.500000m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE
        };
        ctx2.Set<PaymentAddress>().Add(pa2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task PaymentAddress_Address_Unique()
    {
        // Arrange
        var pa1 = CreateValidPaymentAddress(hdIndex: 10);
        pa1.Address = "TDuplicateAddr12345678901234567890";
        Context.Set<PaymentAddress>().Add(pa1);
        await Context.SaveChangesAsync();

        // Act — create second transaction + PA with same address
        await using var ctx2 = CreateContext();
        var tx2 = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000002",
            ItemAssetId = "12345678902",
            ItemClassId = "98765432102",
            ItemName = "M4A4 | Howl",
            StablecoinType = StablecoinType.USDT,
            Price = 100.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 3.000000m,
            TotalAmount = 103.000000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        ctx2.Set<Transaction>().Add(tx2);
        await ctx2.SaveChangesAsync();

        var pa2 = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx2.Id,
            Address = "TDuplicateAddr12345678901234567890", // same address
            HdWalletIndex = 11,
            ExpectedAmount = 103.000000m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE
        };
        ctx2.Set<PaymentAddress>().Add(pa2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task PaymentAddress_HdWalletIndex_Unique()
    {
        // Arrange
        var pa1 = CreateValidPaymentAddress(hdIndex: 42);
        Context.Set<PaymentAddress>().Add(pa1);
        await Context.SaveChangesAsync();

        // Act — create second transaction + PA with same HdWalletIndex
        await using var ctx2 = CreateContext();
        var tx2 = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000002",
            ItemAssetId = "12345678903",
            ItemClassId = "98765432103",
            ItemName = "AWP | Dragon Lore",
            StablecoinType = StablecoinType.USDT,
            Price = 200.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 6.000000m,
            TotalAmount = 206.000000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        ctx2.Set<Transaction>().Add(tx2);
        await ctx2.SaveChangesAsync();

        var pa2 = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx2.Id,
            Address = $"T{Guid.NewGuid():N}42",
            HdWalletIndex = 42, // same index
            ExpectedAmount = 206.000000m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE
        };
        ctx2.Set<PaymentAddress>().Add(pa2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ========== BlockchainTransaction CRUD Tests ==========

    [Fact]
    public async Task BlockchainTx_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 100);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(paymentAddressId: pa.Id);

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);

        // Assert
        Assert.Equal(BlockchainTransactionType.BUYER_PAYMENT, loaded.Type);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, loaded.Status);
        Assert.Equal(51.500000m, loaded.Amount);
        Assert.Equal(0, loaded.ConfirmationCount);
        Assert.Equal(0, loaded.RetryCount);
    }

    [Fact]
    public async Task BlockchainTx_Update_StatusAndConfirmations()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 101);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Act — move to CONFIRMED
        btx.Status = BlockchainTransactionStatus.CONFIRMED;
        btx.ConfirmationCount = 21;
        btx.ConfirmedAt = DateTime.UtcNow;
        btx.BlockNumber = 12345678;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);

        // Assert
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, loaded.Status);
        Assert.Equal(21, loaded.ConfirmationCount);
        Assert.NotNull(loaded.ConfirmedAt);
        Assert.Equal(12345678, loaded.BlockNumber);
    }

    // ========== BlockchainTransaction Unique Constraint Tests ==========

    [Fact]
    public async Task BlockchainTx_TxHash_Unique_PreventsDuplicates()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 102);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx1 = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        btx1.TxHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345";
        Context.Set<BlockchainTransaction>().Add(btx1);
        await Context.SaveChangesAsync();

        // Act — second with same TxHash
        await using var ctx2 = CreateContext();
        var btx2 = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        btx2.TxHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345";
        ctx2.Set<BlockchainTransaction>().Add(btx2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    public async Task BlockchainTx_TxHash_Null_AllowsMultiple()
    {
        // Arrange — filtered unique: NULL TxHash not constrained
        var pa = CreateValidPaymentAddress(hdIndex: 103);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx1 = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        btx1.TxHash = null;
        var btx2 = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        btx2.TxHash = null;

        // Act
        Context.Set<BlockchainTransaction>().AddRange(btx1, btx2);
        await Context.SaveChangesAsync();

        // Assert — no exception
        Assert.NotEqual(btx1.Id, btx2.Id);
    }

    // ========== Type-Dependent CHECK Constraint Tests ==========

    [Fact]
    public async Task CK_Type_BuyerPayment_Violated_WhenPaymentAddressIdNull()
    {
        // Arrange — BUYER_PAYMENT requires PaymentAddressId NOT NULL
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.BUYER_PAYMENT,
            paymentAddressId: null); // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_BuyerPayment_Violated_WhenActualTokenAddressSet()
    {
        // Arrange — BUYER_PAYMENT requires ActualTokenAddress NULL
        var pa = CreateValidPaymentAddress(hdIndex: 110);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        btx.ActualTokenAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"; // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_BuyerPayment_Satisfied()
    {
        // Arrange — valid BUYER_PAYMENT
        var pa = CreateValidPaymentAddress(hdIndex: 111);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        // PaymentAddressId NOT NULL, ActualTokenAddress NULL → valid

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.BUYER_PAYMENT, loaded.Type);
    }

    [Fact]
    public async Task CK_Type_WrongTokenIncoming_Violated_WhenActualTokenAddressNull()
    {
        // Arrange — WRONG_TOKEN_INCOMING requires ActualTokenAddress NOT NULL
        var pa = CreateValidPaymentAddress(hdIndex: 112);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.WRONG_TOKEN_INCOMING,
            paymentAddressId: pa.Id);
        btx.ActualTokenAddress = null; // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_WrongTokenIncoming_Satisfied()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 113);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.WRONG_TOKEN_INCOMING,
            paymentAddressId: pa.Id);
        btx.ActualTokenAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.WRONG_TOKEN_INCOMING, loaded.Type);
        Assert.NotNull(loaded.ActualTokenAddress);
    }

    [Fact]
    public async Task CK_Type_WrongTokenRefund_Violated_WhenPaymentAddressIdNotNull()
    {
        // Arrange — WRONG_TOKEN_REFUND requires PaymentAddressId NULL
        var pa = CreateValidPaymentAddress(hdIndex: 114);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            paymentAddressId: pa.Id); // violation — must be NULL
        btx.ActualTokenAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_WrongTokenRefund_Satisfied()
    {
        // Arrange
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.WRONG_TOKEN_REFUND,
            paymentAddressId: null);
        btx.ActualTokenAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.WRONG_TOKEN_REFUND, loaded.Type);
    }

    [Fact]
    public async Task CK_Type_SpamTokenIncoming_Violated_WhenActualTokenAddressNull()
    {
        // Arrange — SPAM_TOKEN_INCOMING requires ActualTokenAddress NOT NULL, PaymentAddressId NOT NULL
        var pa = CreateValidPaymentAddress(hdIndex: 115);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SPAM_TOKEN_INCOMING,
            status: BlockchainTransactionStatus.CONFIRMED,
            paymentAddressId: pa.Id);
        btx.ActualTokenAddress = null; // violation
        btx.ConfirmationCount = 20;
        btx.ConfirmedAt = DateTime.UtcNow;

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_SpamTokenIncoming_Satisfied()
    {
        // Arrange — SPAM_TOKEN_INCOMING: terminal CONFIRMED status per spec
        var pa = CreateValidPaymentAddress(hdIndex: 116);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SPAM_TOKEN_INCOMING,
            status: BlockchainTransactionStatus.CONFIRMED,
            paymentAddressId: pa.Id);
        btx.ActualTokenAddress = "TUnknownToken12345678901234567890";
        btx.ConfirmationCount = 20;
        btx.ConfirmedAt = DateTime.UtcNow;

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.SPAM_TOKEN_INCOMING, loaded.Type);
    }

    [Fact]
    public async Task CK_Type_Outbound_Violated_WhenPaymentAddressIdNotNull()
    {
        // Arrange — SELLER_PAYOUT requires PaymentAddressId NULL
        var pa = CreateValidPaymentAddress(hdIndex: 117);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            paymentAddressId: pa.Id); // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Type_Outbound_SellerPayout_Satisfied()
    {
        // Arrange
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            paymentAddressId: null);

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.SELLER_PAYOUT, loaded.Type);
    }

    [Fact]
    public async Task CK_Type_Outbound_BuyerRefund_Satisfied()
    {
        // Arrange
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.BUYER_REFUND,
            paymentAddressId: null);

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionType.BUYER_REFUND, loaded.Type);
    }

    // ========== Status-Dependent CHECK Constraint Tests ==========

    [Fact]
    public async Task CK_Status_Confirmed_Violated_WhenConfirmationCountLow()
    {
        // Arrange — CONFIRMED requires ConfirmationCount >= 20
        var pa = CreateValidPaymentAddress(hdIndex: 120);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            status: BlockchainTransactionStatus.CONFIRMED,
            paymentAddressId: pa.Id);
        btx.ConfirmationCount = 19; // violation: < 20
        btx.ConfirmedAt = DateTime.UtcNow;

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Status_Confirmed_Violated_WhenConfirmedAtNull()
    {
        // Arrange — CONFIRMED requires ConfirmedAt NOT NULL
        var pa = CreateValidPaymentAddress(hdIndex: 121);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            status: BlockchainTransactionStatus.CONFIRMED,
            paymentAddressId: pa.Id);
        btx.ConfirmationCount = 20;
        btx.ConfirmedAt = null; // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Status_Confirmed_Satisfied()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 122);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            status: BlockchainTransactionStatus.CONFIRMED,
            paymentAddressId: pa.Id);
        btx.ConfirmationCount = 20;
        btx.ConfirmedAt = DateTime.UtcNow;

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, loaded.Status);
        Assert.True(loaded.ConfirmationCount >= 20);
    }

    [Fact]
    public async Task CK_Status_Detected_Violated_WhenConfirmationCountNonZero()
    {
        // Arrange — DETECTED requires ConfirmationCount = 0
        var pa = CreateValidPaymentAddress(hdIndex: 123);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            status: BlockchainTransactionStatus.DETECTED,
            paymentAddressId: pa.Id);
        btx.ConfirmationCount = 5; // violation: must be 0

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Status_Pending_Violated_WhenConfirmationCountHigh()
    {
        // Arrange — PENDING requires ConfirmationCount < 20
        var pa = CreateValidPaymentAddress(hdIndex: 125);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(
            status: BlockchainTransactionStatus.PENDING,
            paymentAddressId: pa.Id);
        btx.ConfirmationCount = 20; // violation: must be < 20

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Status_Failed_Violated_WhenConfirmedAtNotNull()
    {
        // Arrange — FAILED requires ConfirmedAt NULL
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            status: BlockchainTransactionStatus.FAILED,
            paymentAddressId: null);
        btx.ConfirmedAt = DateTime.UtcNow; // violation

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CK_Status_Failed_Satisfied()
    {
        // Arrange
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            status: BlockchainTransactionStatus.FAILED,
            paymentAddressId: null);
        btx.RetryCount = 3;
        btx.ErrorMessage = "Insufficient energy";
        // ConfirmedAt = null → valid

        // Act
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<BlockchainTransaction>().FirstAsync(b => b.Id == btx.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, loaded.Status);
        Assert.Equal(3, loaded.RetryCount);
    }

    // ========== FK Enforcement Tests ==========

    [Fact]
    public async Task PaymentAddress_FK_Transaction_EnforcedByDatabase()
    {
        // Arrange — non-existent transaction
        var pa = CreateValidPaymentAddress(hdIndex: 130);
        pa.TransactionId = Guid.NewGuid(); // doesn't exist

        Context.Set<PaymentAddress>().Add(pa);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task BlockchainTx_FK_Transaction_EnforcedByDatabase()
    {
        // Arrange — non-existent transaction
        var btx = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            paymentAddressId: null);
        btx.TransactionId = Guid.NewGuid(); // doesn't exist

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task BlockchainTx_FK_PaymentAddress_EnforcedByDatabase()
    {
        // Arrange — non-existent payment address
        var btx = CreateValidBlockchainTx(paymentAddressId: Guid.NewGuid()); // doesn't exist

        Context.Set<BlockchainTransaction>().Add(btx);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    // ========== Navigation Property Tests ==========

    [Fact]
    public async Task PaymentAddress_Navigation_LoadsFromTransaction()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 140);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>()
            .Include(t => t.PaymentAddress)
            .FirstAsync(t => t.Id == _transaction.Id);

        // Assert
        Assert.NotNull(loaded.PaymentAddress);
        Assert.Equal(pa.Id, loaded.PaymentAddress.Id);
    }

    [Fact]
    public async Task BlockchainTransactions_Navigation_LoadsFromTransaction()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 141);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx1 = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        var btx2 = CreateValidBlockchainTx(
            type: BlockchainTransactionType.SELLER_PAYOUT,
            paymentAddressId: null);
        Context.Set<BlockchainTransaction>().AddRange(btx1, btx2);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<Transaction>()
            .Include(t => t.BlockchainTransactions)
            .FirstAsync(t => t.Id == _transaction.Id);

        // Assert
        Assert.Equal(2, loaded.BlockchainTransactions.Count);
    }

    [Fact]
    public async Task BlockchainTransactions_Navigation_LoadsFromPaymentAddress()
    {
        // Arrange
        var pa = CreateValidPaymentAddress(hdIndex: 142);
        Context.Set<PaymentAddress>().Add(pa);
        await Context.SaveChangesAsync();

        var btx = CreateValidBlockchainTx(paymentAddressId: pa.Id);
        Context.Set<BlockchainTransaction>().Add(btx);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<PaymentAddress>()
            .Include(p => p.BlockchainTransactions)
            .FirstAsync(p => p.Id == pa.Id);

        // Assert
        Assert.Single(loaded.BlockchainTransactions);
    }
}
