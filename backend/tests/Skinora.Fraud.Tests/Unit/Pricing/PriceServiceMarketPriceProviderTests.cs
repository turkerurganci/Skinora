using Skinora.Fraud.Application.Pricing;
using Skinora.Shared.Enums;

namespace Skinora.Fraud.Tests.Unit.Pricing;

/// <summary>
/// Unit coverage for the WP4a price bridge (<see cref="PriceServiceMarketPriceProvider"/>)
/// that connects the Transactions <c>IMarketPriceProvider</c> seam to the T81
/// <see cref="IPriceService"/> stack. Verifies the empty-key fail-open guard
/// and straight-through delegation without touching the real cache/HTTP stack.
/// </summary>
public class PriceServiceMarketPriceProviderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyMarketHashName_ReturnsNull_WithoutCallingPriceService(string key)
    {
        var price = new RecordingPriceService();
        var sut = new PriceServiceMarketPriceProvider(price);

        var result = await sut.TryGetMarketPriceAsync(key, StablecoinType.USDT, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, price.CallCount); // guarded before the cache/HTTP stack
    }

    [Fact]
    public async Task NonEmptyKey_DelegatesToPriceService_AndReturnsValue()
    {
        var price = new RecordingPriceService { Result = 13.10m };
        var sut = new PriceServiceMarketPriceProvider(price);

        var result = await sut.TryGetMarketPriceAsync(
            "AK-47 | Redline (Field-Tested)", StablecoinType.USDC, CancellationToken.None);

        Assert.Equal(13.10m, result);
        Assert.Equal(1, price.CallCount);
        // The canonical market_hash_name reaches the stack verbatim — not a
        // classId or display name (the wrong-key trap the bridge avoids).
        Assert.Equal("AK-47 | Redline (Field-Tested)", price.LastKey);
    }

    [Fact]
    public async Task PriceServiceReturnsNull_PropagatesNull_FailOpen()
    {
        // Steam outage / no-price degrades to null in PriceService (08 §7.4);
        // the bridge must propagate it (no flag on missing data), never throw.
        var price = new RecordingPriceService { Result = null };
        var sut = new PriceServiceMarketPriceProvider(price);

        var result = await sut.TryGetMarketPriceAsync("X", StablecoinType.USDT, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, price.CallCount);
    }

    private sealed class RecordingPriceService : IPriceService
    {
        public decimal? Result { get; set; }
        public int CallCount { get; private set; }
        public string? LastKey { get; private set; }

        public Task<decimal?> GetMarketPriceAsync(
            string marketHashName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastKey = marketHashName;
            return Task.FromResult(Result);
        }
    }
}
