using Microsoft.Extensions.Configuration;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Unit.PaymentAddresses;

/// <summary>
/// T172a — the deposit index floor rule (DEPLOY_RUNBOOK §N.8 (b)). ⚠ PARA.
/// The allocator itself is covered on SQL Server in
/// <c>PaymentAddressAllocatorTests</c>.
/// </summary>
public class PaymentAddressIndexFloorOptionsTests
{
    [Theory]
    [InlineData(null, 0, 0)]     // first deposit, no floor
    [InlineData(null, 100, 100)] // first deposit after a restore of an empty database
    [InlineData(4, 0, 5)]        // no floor: MAX + 1
    [InlineData(4, 41, 41)]      // floor above MAX + 1: the floor
    [InlineData(40, 41, 41)]     // floor exactly MAX + 1
    [InlineData(41, 41, 42)]     // floor at MAX: MAX + 1 — never the used index
    [InlineData(99, 10, 100)]    // floor below MAX: ignored, never a reuse
    [InlineData(4, -7, 5)]       // a negative floor never lowers anything
    public void NextIndex_IsNeverBelowMaxPlusOne_AndNeverBelowTheFloor(int? max, int floor, int expected)
    {
        var next = PaymentAddressIndexFloorOptions.NextIndex(max, floor);

        Assert.Equal(expected, next);
        Assert.True(next > (max ?? -1));
        Assert.True(next >= floor);
    }

    [Fact]
    public void Binds_From_The_Deployment_Environment_Shape_Per_Network()
    {
        // PaymentAddresses__MinimumNextIndex__TRON=5000 in the environment.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentAddresses:MinimumNextIndex:TRON"] = "5000",
                ["PaymentAddresses:MinimumNextIndex:ETHEREUM"] = "120",
            })
            .Build();
        var options = new PaymentAddressIndexFloorOptions();
        configuration.GetSection(PaymentAddressIndexFloorOptions.SectionName).Bind(options);

        Assert.Equal(5000, options.FloorFor(BlockchainNetwork.TRON));
        Assert.Equal(120, options.FloorFor(BlockchainNetwork.ETHEREUM));
        Assert.Empty(options.Errors());
    }

    [Fact]
    public void A_Network_Without_A_Floor_Has_None()
    {
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["tron"] = 9 } };

        Assert.Equal(9, options.FloorFor(BlockchainNetwork.TRON)); // case-insensitive key
        Assert.Equal(0, options.FloorFor(BlockchainNetwork.ETHEREUM));
    }

    [Theory]
    [InlineData("TRONN", 5)]   // a typo would silently not apply — refused at start-up
    [InlineData("0", 5)]       // a numeric enum value is not a network name
    [InlineData("TRON", -1)]   // negative
    public void Errors_RefuseAnUnknownNetworkOrANegativeFloor(string key, int value)
    {
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { [key] = value } };

        Assert.NotEmpty(options.Errors());
    }
}
