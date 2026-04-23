using Skinora.Auth.Application.ReAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class ReAuthTokenValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsPayloadOnce()
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);
        var payload = new ReAuthTokenPayload(Guid.NewGuid(), "76561198000000003");
        await store.IssueAsync("xyz-token", payload, TimeSpan.FromMinutes(5), default);
        var validator = new ReAuthTokenValidator(store);

        var first = await validator.ValidateAsync("xyz-token", default);
        var second = await validator.ValidateAsync("xyz-token", default);

        Assert.Equal(payload, first);
        Assert.Null(second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_NullOrWhitespaceInput_ReturnsNull(string? token)
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);
        var validator = new ReAuthTokenValidator(store);

        var result = await validator.ValidateAsync(token, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        var store = new InMemoryReAuthTokenStore(TimeProvider.System);
        var validator = new ReAuthTokenValidator(store);

        var result = await validator.ValidateAsync("never-issued", default);

        Assert.Null(result);
    }
}
