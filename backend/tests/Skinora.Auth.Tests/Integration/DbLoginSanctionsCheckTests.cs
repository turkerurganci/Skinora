using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

/// <summary>
/// T146 — login screening checks every saved address on its OWN network's
/// active list (06 §1.4: match key is (network, canonical address)).
/// </summary>
public class DbLoginSanctionsCheckTests : IntegrationTestBase
{
    static DbLoginSanctionsCheckTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SteamId = "76561198000000070";
    private const string TronAddress = "THfUtCxb8h8unh2SMVPjokNfNuVAZT16Ja";
    private const string EvmAddress = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";

    private DbLoginSanctionsCheck CreateSut() =>
        new(Context, new SanctionedAddressLookup(Context));

    private async Task SeedUserAsync(params UserWalletAddress[] wallets)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SteamId,
            SteamDisplayName = "SanctionsTester",
            PreferredLanguage = "en",
        };
        foreach (var w in wallets) user.WalletAddresses.Add(w);
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
    }

    private async Task ListAsync(BlockchainNetwork network, string address)
    {
        Context.Set<SanctionedAddress>().Add(new SanctionedAddress
        {
            Id = Guid.NewGuid(),
            Address = address,
            Network = network,
            Source = SanctionedAddressSources.Manual,
            ListedAt = DateTime.UtcNow,
            IsActive = true,
        });
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task EthereumRefundAddress_OnEthereumList_IsFlagged()
    {
        await SeedUserAsync(UserWalletAddress.For(
            BlockchainNetwork.ETHEREUM, WalletAddressType.REFUND, EvmAddress));
        await ListAsync(BlockchainNetwork.ETHEREUM, EvmAddress);

        var decision = await CreateSut().EvaluateAsync(SteamId, CancellationToken.None);

        Assert.True(decision.IsMatch);
        Assert.Equal(SanctionedAddressSources.Manual, decision.MatchedList);
    }

    [Fact]
    public async Task TronAddress_DifferingOnlyInLetterCase_IsNotFlagged()
    {
        var differsInCase = TronAddress.ToLowerInvariant();
        Assert.NotEqual(TronAddress, differsInCase);
        await SeedUserAsync(UserWalletAddress.For(
            BlockchainNetwork.TRON, WalletAddressType.PAYOUT, TronAddress));
        await ListAsync(BlockchainNetwork.TRON, differsInCase);

        var decision = await CreateSut().EvaluateAsync(SteamId, CancellationToken.None);

        Assert.False(decision.IsMatch);
    }

    [Fact]
    public async Task TronAddress_ExactMatch_IsFlagged()
    {
        await SeedUserAsync(UserWalletAddress.For(
            BlockchainNetwork.TRON, WalletAddressType.PAYOUT, TronAddress));
        await ListAsync(BlockchainNetwork.TRON, TronAddress);

        var decision = await CreateSut().EvaluateAsync(SteamId, CancellationToken.None);

        Assert.True(decision.IsMatch);
    }
}
