using Skinora.API.Services;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Admin;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T157 — the admin transaction list (AD6) and detail (AD7) say which network a
/// transaction pays on, and the list can be filtered by it (07 §1.3 "Admin").
/// The admin detail needs it to link a hash to Etherscan or Tronscan; a hash
/// shown against the wrong explorer is a dead link at best.
/// </summary>
public class AdminTransactionNetworkTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static AdminTransactionNetworkTests() => RegisterAllModules();

    private User _seller = null!;
    private Transaction _tron = null!;
    private Transaction _ethereum = null!;
    private Transaction _listing = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000001101", SteamDisplayName = "T157 List Seller" };
        context.Set<User>().Add(_seller);
        _tron = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        _ethereum = NewTransaction(_seller.Id, network: BlockchainNetwork.ETHEREUM);
        _listing = NewTransaction(_seller.Id);
        context.Set<Transaction>().AddRange(_tron, _ethereum, _listing);
        await context.SaveChangesAsync();
    }

    private static AdminTransactionListQuery Query(BlockchainNetwork? network = null) =>
        new(null, null, null, null, null, null, null, null, null, null, 1, 50, network);

    [Fact]
    public async Task List_CarriesTheNetworkOfEachTransaction_AndNullBeforeAcceptance()
    {
        await using var ctx = CreateContext();
        var page = await new AdminTransactionQueryService(ctx).ListAsync(Query(), CancellationToken.None);

        Assert.Equal(BlockchainNetwork.TRON, page.Items.Single(i => i.Id == _tron.Id).Network);
        Assert.Equal(BlockchainNetwork.ETHEREUM, page.Items.Single(i => i.Id == _ethereum.Id).Network);
        Assert.Null(page.Items.Single(i => i.Id == _listing.Id).Network);
    }

    [Theory]
    [InlineData(BlockchainNetwork.TRON)]
    [InlineData(BlockchainNetwork.ETHEREUM)]
    public async Task List_FilteredByNetwork_ReturnsOnlyThatNetworksTransactions(BlockchainNetwork network)
    {
        await using var ctx = CreateContext();
        var page = await new AdminTransactionQueryService(ctx).ListAsync(Query(network), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(network, item.Network);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Detail_CarriesTheNetwork()
    {
        await using var ctx = CreateContext();
        var service = new AdminTransactionQueryService(ctx);

        Assert.Equal(BlockchainNetwork.ETHEREUM,
            (await service.GetDetailAsync(_ethereum.Id, CancellationToken.None))!.Network);
        Assert.Equal(BlockchainNetwork.TRON,
            (await service.GetDetailAsync(_tron.Id, CancellationToken.None))!.Network);
        Assert.Null((await service.GetDetailAsync(_listing.Id, CancellationToken.None))!.Network);
    }

    [Fact]
    public async Task ListForUser_CarriesTheNetwork()
    {
        await using var ctx = CreateContext();
        var page = await new AdminTransactionQueryService(ctx)
            .ListForUserAsync(_seller.SteamId, 1, 50, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(BlockchainNetwork.ETHEREUM, page!.Items.Single(i => i.Id == _ethereum.Id).Network);
    }
}
