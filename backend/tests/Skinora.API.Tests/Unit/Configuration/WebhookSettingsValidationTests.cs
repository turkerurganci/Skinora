using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skinora.API.Middleware;

namespace Skinora.API.Tests.Unit.Configuration;

/// <summary>
/// T148 düzeltme turu 1 (F3) — two networks sharing one webhook secret fail
/// the start; every other combination (distinct, one blank, both blank) starts.
/// The host-level twin is
/// <c>BlockchainWebhookEndpointTests.Host_Refuses_To_Start_When_Tron_And_Ethereum_Share_A_Webhook_Secret</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WebhookSettingsValidationTests
{
    [Fact]
    public void The_Same_Secret_On_Both_Networks_Fails_Startup_Validation()
    {
        using var provider = Build("same-secret-32-chars-long!!!!!!!!", "same-secret-32-chars-long!!!!!!!!");

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(WebhookSettings.SharedSecretConflictMessage, ex.Failures);
    }

    [Theory]
    [InlineData("tron-secret-32-chars-long!!!!!!!!", "ethereum-secret-32-chars-long!!!!")]
    [InlineData("tron-secret-32-chars-long!!!!!!!!", "")]
    [InlineData("", "")]
    public void Distinct_Or_Blank_Secrets_Start(string tron, string ethereum)
    {
        using var provider = Build(tron, ethereum);

        provider.GetRequiredService<IStartupValidator>().Validate();
        var settings = provider.GetRequiredService<IOptions<WebhookSettings>>().Value;
        Assert.Equal(tron, settings.BlockchainSharedSecret);
        Assert.Equal(ethereum, settings.EthereumSharedSecret);
    }

    private static ServiceProvider Build(string tron, string ethereum)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webhook:BlockchainSharedSecret"] = tron,
                ["Webhook:EthereumSharedSecret"] = ethereum,
            })
            .Build();
        var services = new ServiceCollection();
        WebhookSettings.AddWebhookSettings(services, configuration);
        return services.BuildServiceProvider();
    }
}
