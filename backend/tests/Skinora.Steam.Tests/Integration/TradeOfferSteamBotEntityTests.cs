using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// Integration tests for TradeOffer and PlatformSteamBot entities (T21).
/// Verifies CRUD, check constraints, unique constraints, FK enforcement,
/// and soft delete against a real SQL Server instance via TestContainers.
/// </summary>
public class TradeOfferSteamBotEntityTests : IntegrationTestBase
{
    static TradeOfferSteamBotEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
    }

    private User _seller = null!;
    private Transaction _transaction = null!;
    private PlatformSteamBot _bot = null!;

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

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999001",
            DisplayName = "EscrowBot-01",
            Status = PlatformSteamBotStatus.ACTIVE
        };
        context.Set<PlatformSteamBot>().Add(_bot);
        await context.SaveChangesAsync();

        _transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = _seller.Id,
            EscrowBotId = _bot.Id,
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

    private TradeOffer CreateValidTradeOffer(
        TradeOfferStatus status = TradeOfferStatus.PENDING,
        TradeOfferDirection direction = TradeOfferDirection.TO_SELLER)
    {
        return new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = _transaction.Id,
            PlatformSteamBotId = _bot.Id,
            Direction = direction,
            Status = status,
            RetryCount = 0
        };
    }

    // ========== PlatformSteamBot CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlatformSteamBot_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999010",
            DisplayName = "EscrowBot-10",
            Status = PlatformSteamBotStatus.ACTIVE
        };

        // Act
        Context.Set<PlatformSteamBot>().Add(bot);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<PlatformSteamBot>().FirstAsync(b => b.Id == bot.Id);

        // Assert
        Assert.Equal("76561198099999010", loaded.SteamId);
        Assert.Equal("EscrowBot-10", loaded.DisplayName);
        Assert.Equal(PlatformSteamBotStatus.ACTIVE, loaded.Status);
        Assert.Equal(0, loaded.ActiveEscrowCount);
        Assert.Equal(0, loaded.DailyTradeOfferCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlatformSteamBot_Update_DenormalizedCounters()
    {
        // Arrange — fetch from test context so it tracks changes
        var bot = await Context.Set<PlatformSteamBot>().FirstAsync(b => b.Id == _bot.Id);

        // Act
        bot.ActiveEscrowCount = 5;
        bot.DailyTradeOfferCount = 12;
        bot.LastHealthCheckAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<PlatformSteamBot>().FirstAsync(b => b.Id == _bot.Id);

        // Assert
        Assert.Equal(5, loaded.ActiveEscrowCount);
        Assert.Equal(12, loaded.DailyTradeOfferCount);
        Assert.NotNull(loaded.LastHealthCheckAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlatformSteamBot_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999020",
            DisplayName = "EscrowBot-Deleted",
            Status = PlatformSteamBotStatus.OFFLINE,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        Context.Set<PlatformSteamBot>().Add(bot);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<PlatformSteamBot>().Where(b => b.Id == bot.Id).ToListAsync();
        var unfiltered = await readCtx.Set<PlatformSteamBot>().IgnoreQueryFilters().Where(b => b.Id == bot.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== PlatformSteamBot Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlatformSteamBot_SteamId_Unique()
    {
        // Arrange — bot with same SteamId
        await using var ctx2 = CreateContext();
        var dup = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = _bot.SteamId, // duplicate
            DisplayName = "DuplicateBot",
            Status = PlatformSteamBotStatus.ACTIVE
        };
        ctx2.Set<PlatformSteamBot>().Add(dup);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ========== TradeOffer CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var offer = CreateValidTradeOffer();

        // Act
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);

        // Assert
        Assert.Equal(_transaction.Id, loaded.TransactionId);
        Assert.Equal(_bot.Id, loaded.PlatformSteamBotId);
        Assert.Equal(TradeOfferDirection.TO_SELLER, loaded.Direction);
        Assert.Equal(TradeOfferStatus.PENDING, loaded.Status);
        Assert.Equal(0, loaded.RetryCount);
        Assert.Null(loaded.SteamTradeOfferId);
        Assert.Null(loaded.SentAt);
        Assert.Null(loaded.RespondedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_Update_StatusAndTimestamps()
    {
        // Arrange
        var offer = CreateValidTradeOffer();
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Act — transition to SENT
        offer.Status = TradeOfferStatus.SENT;
        offer.SteamTradeOfferId = "1234567890";
        offer.SentAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);

        // Assert
        Assert.Equal(TradeOfferStatus.SENT, loaded.Status);
        Assert.Equal("1234567890", loaded.SteamTradeOfferId);
        Assert.NotNull(loaded.SentAt);
    }

    // ========== TradeOffer Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_SteamTradeOfferId_Unique_PreventsDuplicates()
    {
        // Arrange
        var offer1 = CreateValidTradeOffer(status: TradeOfferStatus.SENT);
        offer1.SteamTradeOfferId = "9999999999";
        offer1.SentAt = DateTime.UtcNow;
        Context.Set<TradeOffer>().Add(offer1);
        await Context.SaveChangesAsync();

        // Act — second with same SteamTradeOfferId
        await using var ctx2 = CreateContext();
        var offer2 = CreateValidTradeOffer(status: TradeOfferStatus.SENT);
        offer2.SteamTradeOfferId = "9999999999"; // duplicate
        offer2.SentAt = DateTime.UtcNow;
        ctx2.Set<TradeOffer>().Add(offer2);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_SteamTradeOfferId_Null_AllowsMultiple()
    {
        // Arrange — filtered unique: NULL values not constrained
        var offer1 = CreateValidTradeOffer();
        offer1.SteamTradeOfferId = null;
        var offer2 = CreateValidTradeOffer();
        offer2.SteamTradeOfferId = null;

        // Act
        Context.Set<TradeOffer>().AddRange(offer1, offer2);
        await Context.SaveChangesAsync();

        // Assert — no exception
        Assert.NotEqual(offer1.Id, offer2.Id);
    }

    // ========== TradeOffer State-Dependent CHECK Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Sent_Violated_WhenSentAtNull()
    {
        // Arrange — SENT requires SentAt NOT NULL
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.SENT);
        offer.SentAt = null; // violation

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Sent_Satisfied()
    {
        // Arrange
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.SENT);
        offer.SteamTradeOfferId = "1111111111";
        offer.SentAt = DateTime.UtcNow;

        // Act
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);
        Assert.Equal(TradeOfferStatus.SENT, loaded.Status);
        Assert.NotNull(loaded.SentAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Accepted_Violated_WhenSentAtNull()
    {
        // Arrange — ACCEPTED requires SentAt NOT NULL
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.ACCEPTED);
        offer.SentAt = null; // violation
        offer.RespondedAt = DateTime.UtcNow;

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Accepted_Violated_WhenRespondedAtNull()
    {
        // Arrange — ACCEPTED requires RespondedAt NOT NULL
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.ACCEPTED);
        offer.SentAt = DateTime.UtcNow;
        offer.RespondedAt = null; // violation

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Accepted_Satisfied()
    {
        // Arrange
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.ACCEPTED);
        offer.SteamTradeOfferId = "2222222222";
        offer.SentAt = DateTime.UtcNow.AddMinutes(-5);
        offer.RespondedAt = DateTime.UtcNow;

        // Act
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);
        Assert.Equal(TradeOfferStatus.ACCEPTED, loaded.Status);
        Assert.NotNull(loaded.SentAt);
        Assert.NotNull(loaded.RespondedAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Declined_Violated_WhenRespondedAtNull()
    {
        // Arrange — DECLINED requires SentAt + RespondedAt NOT NULL
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.DECLINED);
        offer.SentAt = DateTime.UtcNow;
        offer.RespondedAt = null; // violation

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Declined_Satisfied()
    {
        // Arrange
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.DECLINED);
        offer.SteamTradeOfferId = "3333333333";
        offer.SentAt = DateTime.UtcNow.AddMinutes(-5);
        offer.RespondedAt = DateTime.UtcNow;

        // Act
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);
        Assert.Equal(TradeOfferStatus.DECLINED, loaded.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Expired_Violated_WhenSentAtNull()
    {
        // Arrange — EXPIRED requires SentAt NOT NULL
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.EXPIRED);
        offer.SentAt = null; // violation
        offer.RespondedAt = DateTime.UtcNow;

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Expired_Satisfied()
    {
        // Arrange
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.EXPIRED);
        offer.SteamTradeOfferId = "4444444444";
        offer.SentAt = DateTime.UtcNow.AddMinutes(-30);
        offer.RespondedAt = DateTime.UtcNow;

        // Act
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);
        Assert.Equal(TradeOfferStatus.EXPIRED, loaded.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CK_TradeOffer_Failed_SentAtNull_Allowed()
    {
        // Arrange — FAILED: SentAt NOT required (pre-send failure is valid)
        var offer = CreateValidTradeOffer(status: TradeOfferStatus.FAILED);
        offer.SentAt = null; // allowed per 06 §3.9
        offer.ErrorMessage = "Steam API unreachable";

        // Act — should not throw
        Context.Set<TradeOffer>().Add(offer);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<TradeOffer>().FirstAsync(t => t.Id == offer.Id);
        Assert.Equal(TradeOfferStatus.FAILED, loaded.Status);
        Assert.Null(loaded.SentAt);
    }

    // ========== FK Enforcement Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_FK_Transaction_EnforcedByDatabase()
    {
        // Arrange — non-existent transaction
        var offer = CreateValidTradeOffer();
        offer.TransactionId = Guid.NewGuid(); // doesn't exist

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TradeOffer_FK_PlatformSteamBot_EnforcedByDatabase()
    {
        // Arrange — non-existent bot
        var offer = CreateValidTradeOffer();
        offer.PlatformSteamBotId = Guid.NewGuid(); // doesn't exist

        Context.Set<TradeOffer>().Add(offer);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Transaction_FK_EscrowBotId_EnforcedByDatabase()
    {
        // Arrange — transaction with non-existent EscrowBotId
        await using var ctx2 = CreateContext();
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CREATED,
            SellerId = _seller.Id,
            EscrowBotId = Guid.NewGuid(), // doesn't exist
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000099",
            ItemAssetId = "99999999901",
            ItemClassId = "99999999901",
            ItemName = "Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 10.000000m,
            CommissionRate = 0.0300m,
            CommissionAmount = 0.300000m,
            TotalAmount = 10.300000m,
            SellerPayoutAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            PaymentTimeoutMinutes = 60
        };
        ctx2.Set<Transaction>().Add(tx);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }
}
