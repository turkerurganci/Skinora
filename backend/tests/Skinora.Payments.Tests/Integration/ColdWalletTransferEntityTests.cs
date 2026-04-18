using Microsoft.EntityFrameworkCore;
using Skinora.Payments.Domain.Entities;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Payments.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ColdWalletTransfer"/> (T25, 06 §3.22).
/// Verifies CRUD, unique TxHash and append-only immutability.
/// </summary>
public class ColdWalletTransferEntityTests : IntegrationTestBase
{
    static ColdWalletTransferEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PaymentsModuleDbRegistration.RegisterPaymentsModule();
    }

    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000010",
            SteamDisplayName = "Admin-A"
        };
        context.Set<User>().Add(_admin);
        await context.SaveChangesAsync();
    }

    private ColdWalletTransfer CreateValid(string txHash = "0xdeadbeef0000000000000000000000000000000000000000000000000000")
    {
        return new ColdWalletTransfer
        {
            Amount = 1000.000000m,
            Token = StablecoinType.USDT,
            FromAddress = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL",
            ToAddress = "TYqjQbLmKcYtVjN4HpR5sXzW2eB3vM8kA1",
            TxHash = txHash,
            InitiatedByAdminId = _admin.Id,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ColdWalletTransfer_Insert_And_Read_RoundTrips()
    {
        var transfer = CreateValid();

        Context.Set<ColdWalletTransfer>().Add(transfer);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<ColdWalletTransfer>().FirstAsync(c => c.Id == transfer.Id);

        Assert.Equal(1000.000000m, loaded.Amount);
        Assert.Equal(StablecoinType.USDT, loaded.Token);
        Assert.Equal(transfer.TxHash, loaded.TxHash);
        Assert.Equal(_admin.Id, loaded.InitiatedByAdminId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ColdWalletTransfer_DuplicateTxHash_Rejected()
    {
        var first = CreateValid("0xabc000000000000000000000000000000000000000000000000000000000");
        Context.Set<ColdWalletTransfer>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid("0xabc000000000000000000000000000000000000000000000000000000000");
        ctx.Set<ColdWalletTransfer>().Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ColdWalletTransfer_Update_Rejected_By_AppendOnlyGuard()
    {
        var transfer = CreateValid();
        Context.Set<ColdWalletTransfer>().Add(transfer);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<ColdWalletTransfer>().FirstAsync(c => c.Id == transfer.Id);
        tracked.Amount = 2000.000000m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ColdWalletTransfer_Delete_Rejected_By_AppendOnlyGuard()
    {
        var transfer = CreateValid();
        Context.Set<ColdWalletTransfer>().Add(transfer);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<ColdWalletTransfer>().FirstAsync(c => c.Id == transfer.Id);
        Context.Set<ColdWalletTransfer>().Remove(tracked);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(StablecoinType.USDT)]
    [InlineData(StablecoinType.USDC)]
    [Trait("Category", "Integration")]
    public async Task ColdWalletTransfer_BothStablecoins_Persist(StablecoinType token)
    {
        var transfer = CreateValid($"0x{token}{Guid.NewGuid():N}".PadRight(66, '0')[..66]);
        transfer.Token = token;

        Context.Set<ColdWalletTransfer>().Add(transfer);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<ColdWalletTransfer>().FirstAsync(c => c.Id == transfer.Id);
        Assert.Equal(token, loaded.Token);
    }
}
