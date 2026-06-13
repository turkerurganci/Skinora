using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Admin;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T103b-2 — AdminBotRecoveryService (AD25/AD26) + AdminSteamBotQueryService
/// failover/recovery-count derivation (AD10) integration tests on a real SQL
/// Server.
/// </summary>
public sealed class AdminBotRecoveryServiceTests : IntegrationTestBase
{
    static AdminBotRecoveryServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;
    private PlatformSteamBot _bot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000501", SteamDisplayName = "Seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000502", SteamDisplayName = "Buyer" };
        _admin = new User { Id = Guid.NewGuid(), SteamId = "76561198000000503", SteamDisplayName = "Admin" };
        context.Set<User>().AddRange(_seller, _buyer, _admin);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999501",
            DisplayName = "EscrowBot-AD25",
            Status = PlatformSteamBotStatus.RESTRICTED,
            RestrictionReason = "restricted",
        };
        context.Set<PlatformSteamBot>().Add(_bot);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetQueue_ReturnsRows_WithJoinedTransactionAndParties()
    {
        var txId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        await AddRecoveryItemAsync(txId, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var sut = CreateSut();
        var result = await sut.GetQueueAsync(_bot.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(_bot.Id, result!.BotId);
        var row = Assert.Single(result.Items);
        Assert.Equal(txId, row.TransactionId);
        Assert.Equal("AK-47 | Redline", row.ItemName);
        Assert.Equal(_seller.SteamId, row.SellerSteamId);
        Assert.Equal(_seller.SteamDisplayName, row.SellerDisplayName);
        Assert.Equal(_buyer.SteamId, row.BuyerSteamId);
        Assert.Equal(BotRecoveryStatus.PENDING, row.RecoveryStatus);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, row.StatusAtRestriction);
    }

    [Fact]
    public async Task GetQueue_UnknownBot_ReturnsNull()
    {
        var sut = CreateSut();
        Assert.Null(await sut.GetQueueAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_NoteAssignAndStatus_PersistsAndAudits()
    {
        var txId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await AddRecoveryItemAsync(txId, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, recoveryId,
            new UpdateRecoveryItemRequest(
                RecoveryStatus: BotRecoveryStatus.IN_REVIEW,
                ResponsibleAdminId: _admin.Id,
                AdminNote: "Steam support ticket opened."),
            ipAddress: "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(UpdateRecoveryItemStatus.Updated, outcome.Status);
        Assert.Equal(BotRecoveryStatus.IN_REVIEW, outcome.Body!.RecoveryStatus);
        Assert.Equal(_admin.Id, outcome.Body.ResponsibleAdminId);
        Assert.Equal(_admin.SteamDisplayName, outcome.Body.ResponsibleAdminName);
        Assert.Equal("Steam support ticket opened.", outcome.Body.AdminNote);

        await using var verify = CreateContext();
        var item = await verify.Set<BotRecoveryItem>().SingleAsync(r => r.Id == recoveryId);
        Assert.Equal(BotRecoveryStatus.IN_REVIEW, item.RecoveryStatus);
        Assert.Equal(_admin.Id, item.ResponsibleAdminId);
        Assert.Null(item.ResolvedAt);
        Assert.True(await verify.Set<AuditLog>()
            .AnyAsync(a => a.Action == AuditAction.BOT_RECOVERY_UPDATED && a.EntityId == recoveryId.ToString()));
    }

    [Fact]
    public async Task Update_Resolve_StampsResolvedAt()
    {
        var txId = await AddTransactionAsync(TransactionStatus.CANCELLED_ADMIN, cancelled: true);
        var recoveryId = await AddRecoveryItemAsync(txId, BotRecoveryStatus.IN_REVIEW, TransactionStatus.CANCELLED_ADMIN);

        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, recoveryId,
            new UpdateRecoveryItemRequest(BotRecoveryStatus.RESOLVED, null, null),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(UpdateRecoveryItemStatus.Updated, outcome.Status);
        await using var verify = CreateContext();
        var item = await verify.Set<BotRecoveryItem>().SingleAsync(r => r.Id == recoveryId);
        Assert.Equal(BotRecoveryStatus.RESOLVED, item.RecoveryStatus);
        Assert.NotNull(item.ResolvedAt);
    }

    [Fact]
    public async Task Update_AlreadyResolved_IsRejected()
    {
        var txId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await AddRecoveryItemAsync(txId, BotRecoveryStatus.RESOLVED, TransactionStatus.ITEM_ESCROWED);

        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, recoveryId,
            new UpdateRecoveryItemRequest(BotRecoveryStatus.IN_REVIEW, null, null),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(UpdateRecoveryItemStatus.AlreadyResolved, outcome.Status);
        Assert.Equal(BotRecoveryErrorCodes.AlreadyResolved, outcome.ErrorCode);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, Guid.NewGuid(),
            new UpdateRecoveryItemRequest(BotRecoveryStatus.IN_REVIEW, null, null),
            ipAddress: null, CancellationToken.None);
        Assert.Equal(UpdateRecoveryItemStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task Update_UnknownResponsibleAdmin_IsRejected()
    {
        var txId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await AddRecoveryItemAsync(txId, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, recoveryId,
            new UpdateRecoveryItemRequest(null, Guid.NewGuid(), null),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(UpdateRecoveryItemStatus.ValidationFailed, outcome.Status);
        Assert.Equal(BotRecoveryErrorCodes.ResponsibleAdminNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Update_NoFields_ReturnsValidationFailed()
    {
        var txId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await AddRecoveryItemAsync(txId, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var sut = CreateSut();
        var outcome = await sut.UpdateAsync(
            _admin.Id, recoveryId,
            new UpdateRecoveryItemRequest(null, null, null),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(UpdateRecoveryItemStatus.ValidationFailed, outcome.Status);
        Assert.Equal(BotRecoveryErrorCodes.NoChange, outcome.ErrorCode);
    }

    [Fact]
    public async Task QueryService_DerivesFailoverStatusAndRecoveryCount()
    {
        // Restricted bot _bot gets 2 open + 1 resolved recovery item → IN_RECOVERY / 2.
        var t1 = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED);
        var t2 = await AddTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        var t3 = await AddTransactionAsync(TransactionStatus.CANCELLED_ADMIN, cancelled: true);
        await AddRecoveryItemAsync(t1, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);
        await AddRecoveryItemAsync(t2, BotRecoveryStatus.IN_REVIEW, TransactionStatus.PAYMENT_RECEIVED);
        await AddRecoveryItemAsync(t3, BotRecoveryStatus.RESOLVED, TransactionStatus.CANCELLED_ADMIN);

        // A second restricted bot with no recovery items → DIVERTED / 0; an ACTIVE bot → NONE / 0.
        await using (var arrange = CreateContext())
        {
            arrange.Set<PlatformSteamBot>().AddRange(
                new PlatformSteamBot
                {
                    Id = Guid.NewGuid(),
                    SteamId = "76561198099999777",
                    DisplayName = "EscrowBot-DivertedOnly",
                    Status = PlatformSteamBotStatus.BANNED,
                    RestrictionReason = "banned",
                },
                new PlatformSteamBot
                {
                    Id = Guid.NewGuid(),
                    SteamId = "76561198099999888",
                    DisplayName = "EscrowBot-Active",
                    Status = PlatformSteamBotStatus.ACTIVE,
                });
            await arrange.SaveChangesAsync();
        }

        var query = new AdminSteamBotQueryService(Context);
        var result = await query.ListAsync(CancellationToken.None);

        var restricted = result.Accounts.Single(a => a.Id == _bot.Id);
        Assert.Equal("ACTIVE_TXN_IN_RECOVERY", restricted.FailoverStatus);
        Assert.Equal(2, restricted.RecoveryTransactionCount);
        Assert.Equal("restricted", restricted.RestrictionReason);

        var diverted = result.Accounts.Single(a => a.Name == "EscrowBot-DivertedOnly");
        Assert.Equal("RESTRICTED_NEW_TXN_DIVERTED", diverted.FailoverStatus);
        Assert.Equal(0, diverted.RecoveryTransactionCount);

        var active = result.Accounts.Single(a => a.Name == "EscrowBot-Active");
        Assert.Equal("NONE", active.FailoverStatus);
        Assert.Null(active.RestrictionReason);
    }

    // ---------- helpers ----------

    private AdminBotRecoveryService CreateSut()
        => new(Context, new AuditLogger(Context, TimeProvider.System), TimeProvider.System);

    private async Task<Guid> AddTransactionAsync(TransactionStatus status, bool cancelled = false)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var arrange = CreateContext();
        arrange.Set<Transaction>().Add(new Transaction
        {
            Id = id,
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            EscrowBotId = _bot.Id,
            EscrowBotAssetId = "asset-on-bot",
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            BuyerRefundAddress = "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
            ItemAssetId = "asset-src",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.03m,
            CommissionAmount = 3m,
            TotalAmount = 103m,
            SellerPayoutAddress = "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
            CancelledBy = cancelled ? CancelledByType.ADMIN : null,
            CancelReason = cancelled ? "admin cancel" : null,
            CancelledAt = cancelled ? now : null,
        });
        await arrange.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddRecoveryItemAsync(
        Guid transactionId, BotRecoveryStatus status, TransactionStatus statusAtRestriction)
    {
        var id = Guid.NewGuid();
        await using var arrange = CreateContext();
        arrange.Set<BotRecoveryItem>().Add(new BotRecoveryItem
        {
            Id = id,
            PlatformSteamBotId = _bot.Id,
            TransactionId = transactionId,
            RecoveryStatus = status,
            StatusAtRestriction = statusAtRestriction,
            ResolvedAt = status == BotRecoveryStatus.RESOLVED ? DateTime.UtcNow : null,
        });
        await arrange.SaveChangesAsync();
        return id;
    }
}
