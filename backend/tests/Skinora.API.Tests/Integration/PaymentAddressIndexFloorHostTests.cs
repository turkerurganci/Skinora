using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Skinora.API.Tests.Common;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T172a doğrulama F1 (S1, ⚠ PARA) — the deposit index floor at the level of
/// the real API host: the deployment value (<c>PaymentAddresses__MinimumNextIndex__&lt;NETWORK&gt;</c>)
/// must reach the address allocator, and a value that is not plain digits must
/// stop the backend from starting instead of leaving it with no floor.
/// </summary>
/// <remarks>
/// What each test pins (removing the line breaks the test):
/// <list type="bullet">
/// <item><c>.Bind(…)</c> in TransactionsModule → <see cref="A_Valid_Floor_Reaches_The_Allocator_And_The_Start_Up_Line"/>.</item>
/// <item><c>.Validate(…)</c> or <c>.ValidateOnStart()</c> → <see cref="An_Invalid_Floor_Stops_The_Start_Even_Without_The_Start_Up_Line"/>.</item>
/// <item><c>AddHostedService&lt;PaymentAddressIndexFloorStartupLog&gt;</c> → the start-up line test.</item>
/// <item>Both start-up checks → <see cref="An_Invalid_Floor_Stops_The_Backend_From_Starting"/>.</item>
/// </list>
/// </remarks>
public class PaymentAddressIndexFloorHostTests
{
    private static HangfireBypassFactory Host(
        IReadOnlyDictionary<string, string> settings,
        bool withoutStartUpLine = false) =>
        new HostWithSettings(settings, withoutStartUpLine);

    [Fact]
    public void A_Valid_Floor_Reaches_The_Allocator_And_The_Start_Up_Line()
    {
        using var factory = Host(new Dictionary<string, string>
        {
            ["PaymentAddresses:MinimumNextIndex:TRON"] = "5000",
            ["PaymentAddresses:MinimumNextIndex:ETHEREUM"] = "",
        });

        using var scope = factory.Services.CreateScope();
        var allocator = Assert.IsType<PaymentAddressAllocator>(
            scope.ServiceProvider.GetRequiredService<IPaymentAddressAllocator>());
        Assert.Equal(5000, allocator.FloorFor(BlockchainNetwork.TRON));
        Assert.Equal(0, allocator.FloorFor(BlockchainNetwork.ETHEREUM));

        // The start-up line is registered and says what the allocator applies.
        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<PaymentAddressIndexFloorStartupLog>());
        var options = factory.Services.GetRequiredService<IOptions<PaymentAddressIndexFloorOptions>>().Value;
        Assert.Equal("TRON = 5000, ETHEREUM = none", PaymentAddressIndexFloorStartupLog.Describe(options));
    }

    [Fact]
    public void Without_A_Setting_The_Backend_Starts_With_No_Floor()
    {
        using var factory = Host(new Dictionary<string, string>());

        using var scope = factory.Services.CreateScope();
        var allocator = Assert.IsType<PaymentAddressAllocator>(
            scope.ServiceProvider.GetRequiredService<IPaymentAddressAllocator>());
        Assert.Equal(0, allocator.FloorFor(BlockchainNetwork.TRON));
        Assert.Equal(0, allocator.FloorFor(BlockchainNetwork.ETHEREUM));
    }

    [Theory]
    [InlineData("TRON", "1.000")]       // Turkish thousands separator — the likely mistake
    [InlineData("TRON", "1,000")]
    [InlineData("TRON", "12 345")]
    [InlineData("TRON", "1e4")]
    [InlineData("TRON", "abc")]
    [InlineData("ETHEREUM", "99999999999")]
    [InlineData("TRON", "-1")]
    [InlineData("TRONN", "5")]
    public void An_Invalid_Floor_Stops_The_Backend_From_Starting(string network, string value)
    {
        using var factory = Host(new Dictionary<string, string>
        {
            [$"PaymentAddresses:MinimumNextIndex:{network}"] = value,
        });

        var error = Record.Exception(() => factory.Services);

        Assert.NotNull(error);
        Assert.Contains("MinimumNextIndex", Flatten(error));
    }

    [Fact]
    public void An_Invalid_Floor_Stops_The_Start_Even_Without_The_Start_Up_Line()
    {
        // The start-up line validates too (it reads the options); without it,
        // ValidateOnStart alone must still refuse the start.
        using var factory = Host(
            new Dictionary<string, string> { ["PaymentAddresses:MinimumNextIndex:TRON"] = "1.000" },
            withoutStartUpLine: true);

        var error = Record.Exception(() => factory.Services);

        Assert.NotNull(error);
        Assert.Contains("MinimumNextIndex", Flatten(error));
    }

    private static string Flatten(Exception error)
    {
        var text = new System.Text.StringBuilder();
        for (var e = error; e is not null; e = e.InnerException)
        {
            text.AppendLine(e.Message);
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) text.AppendLine(Flatten(inner));
            }
        }
        return text.ToString();
    }

    private sealed class HostWithSettings(IReadOnlyDictionary<string, string> settings, bool withoutStartUpLine)
        : HangfireBypassFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            if (withoutStartUpLine)
            {
                builder.ConfigureServices(services =>
                {
                    foreach (var d in services
                                 .Where(d => d.ImplementationType == typeof(PaymentAddressIndexFloorStartupLog))
                                 .ToList())
                    {
                        services.Remove(d);
                    }
                });
            }
        }
    }
}
