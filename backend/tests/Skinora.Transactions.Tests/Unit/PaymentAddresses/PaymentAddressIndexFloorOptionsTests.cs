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
    [InlineData(4, -7, 5)]       // a negative floor (unreachable from configuration) never lowers anything
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
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["tron"] = "9" } };

        Assert.Equal(9, options.FloorFor(BlockchainNetwork.TRON)); // case-insensitive key
        Assert.Equal(0, options.FloorFor(BlockchainNetwork.ETHEREUM));
    }

    [Theory]
    [InlineData("TRONN", "5")]   // a typo would silently not apply — refused at start-up
    [InlineData("0", "5")]       // a numeric enum value is not a network name
    public void Errors_RefuseAnUnknownNetwork(string key, string value)
    {
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { [key] = value } };

        Assert.NotEmpty(options.Errors());
    }

    /// <summary>
    /// T172a doğrulama F1 (⚠ PARA) — every value that is not plain digits
    /// within int is refused, and <see cref="PaymentAddressIndexFloorOptions.FloorFor"/>
    /// never turns it into a silent 0. The Turkish thousands separator
    /// (<c>1.000</c>) is the likely operator mistake.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("12.345")]
    [InlineData("12,345")]
    [InlineData("12 345")]
    [InlineData(" 1000")]
    [InlineData("1000 ")]
    [InlineData("1e4")]
    [InlineData("99999999999")]
    [InlineData("2147483648")]   // int.MaxValue + 1
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("0x10")]
    [InlineData("١٢٣")]          // non-ASCII digits
    public void A_Value_That_Is_Not_Plain_Digits_Is_An_Error_Never_A_Silent_Zero(string value)
    {
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["TRON"] = value } };

        Assert.Single(options.Errors());
        Assert.Throws<InvalidOperationException>(() => options.FloorFor(BlockchainNetwork.TRON));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1000", 1000)]
    [InlineData("0010", 10)]
    [InlineData("2147483647", int.MaxValue)]
    public void Plain_Digits_Within_Int_Are_The_Floor(string value, int expected)
    {
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["ETHEREUM"] = value } };

        Assert.Empty(options.Errors());
        Assert.Equal(expected, options.FloorFor(BlockchainNetwork.ETHEREUM));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Value_Means_No_Floor(string? value)
    {
        // docker-compose passes PAYMENT_ADDRESS_MIN_NEXT_INDEX_TRON:- (empty) when unset.
        var options = new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["TRON"] = value } };

        Assert.Empty(options.Errors());
        Assert.Equal(0, options.FloorFor(BlockchainNetwork.TRON));
    }

    [Fact]
    public void The_Binder_Keeps_An_Unparsable_Value_So_Validation_Sees_It()
    {
        // The defect F1 found: bound as int, "1.000" was dropped by the binder
        // and the options looked valid with no floor. Bound as text it stays.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentAddresses:MinimumNextIndex:TRON"] = "1.000",
            })
            .Build();
        var options = new PaymentAddressIndexFloorOptions();
        configuration.GetSection(PaymentAddressIndexFloorOptions.SectionName).Bind(options);

        Assert.Equal("1.000", options.MinimumNextIndex["TRON"]);
        Assert.NotEmpty(options.Errors());
    }

    [Fact]
    public void NextIndex_Refuses_To_Wrap_Past_The_Last_Index()
    {
        // T172a doğrulama S4-5: MAX = 2^31 − 1 used to wrap to int.MinValue and
        // a later floor of 0 would have handed out index 0 again.
        Assert.Throws<InvalidOperationException>(() => PaymentAddressIndexFloorOptions.NextIndex(int.MaxValue, 0));
        Assert.Throws<InvalidOperationException>(() => PaymentAddressIndexFloorOptions.NextIndex(int.MaxValue, int.MaxValue));
        Assert.Equal(int.MaxValue, PaymentAddressIndexFloorOptions.NextIndex(int.MaxValue - 1, 0));
    }
}
