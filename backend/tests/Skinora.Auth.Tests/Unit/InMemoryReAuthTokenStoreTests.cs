using Microsoft.Extensions.Time.Testing;
using Skinora.Auth.Application.ReAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class InMemoryReAuthTokenStoreTests
{
    [Fact]
    public async Task ConsumeAsync_WithinTtl_ReturnsPayloadAndInvalidatesToken()
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);
        var payload = new ReAuthTokenPayload(Guid.NewGuid(), "76561198000000001");

        await store.IssueAsync("plain-token", payload, TimeSpan.FromMinutes(5), default);

        var first = await store.ConsumeAsync("plain-token", default);
        var second = await store.ConsumeAsync("plain-token", default);

        Assert.Equal(payload, first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_AfterTtlExpires_ReturnsNull()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryReAuthTokenStore(time);

        await store.IssueAsync(
            "plain-token",
            new ReAuthTokenPayload(Guid.NewGuid(), "76561198000000002"),
            TimeSpan.FromMinutes(5),
            default);

        time.Advance(TimeSpan.FromMinutes(6));

        var result = await store.ConsumeAsync("plain-token", default);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeAsync_UnknownToken_ReturnsNull()
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);

        var result = await store.ConsumeAsync("never-issued", default);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumeAsync_EmptyOrNullInput_ReturnsNull()
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);

        Assert.Null(await store.ConsumeAsync(null!, default));
        Assert.Null(await store.ConsumeAsync("", default));
        Assert.Null(await store.ConsumeAsync("   ", default));
    }
}
