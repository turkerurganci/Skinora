using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.Shared.Email;

namespace Skinora.Shared.Tests.Unit.Email;

/// <summary>
/// Unit coverage for <see cref="ResendEmailClient"/> — verifies the 08 §4.3
/// HTTP error-to-exception classification table (T78). Uses a stub
/// <see cref="HttpMessageHandler"/> so no real network call is made.
/// </summary>
public sealed class ResendEmailClientTests
{
    private static readonly ResendSendEmailRequest Sample = new(
        ToAddress: "user@example.com",
        Subject: "Hello",
        HtmlBody: "<p>hello</p>");

    [Fact]
    public async Task SendAsync_Success_ReturnsMessageId()
    {
        var (client, _) = BuildClient(_ =>
            BuildResponse(HttpStatusCode.OK, "{\"id\":\"4e8df4fb-1234-4b1f-8a99-12345\"}"));

        var result = await client.SendAsync(Sample, CancellationToken.None);

        Assert.Equal("4e8df4fb-1234-4b1f-8a99-12345", result.MessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task SendAsync_TransientHttpStatuses_ThrowResendTransientException(HttpStatusCode status)
    {
        var (client, _) = BuildClient(_ =>
            BuildResponse(status, "{\"name\":\"server_error\",\"message\":\"boom\"}"));

        var ex = await Assert.ThrowsAsync<ResendTransientException>(() =>
            client.SendAsync(Sample, CancellationToken.None));

        Assert.Equal((int)status, ex.HttpStatusCode);
        Assert.Equal("server_error", ex.ResendErrorName);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task SendAsync_PermanentHttpStatuses_ThrowResendPermanentException(HttpStatusCode status)
    {
        var (client, _) = BuildClient(_ =>
            BuildResponse(status, "{\"name\":\"validation_error\",\"message\":\"bad email\"}"));

        var ex = await Assert.ThrowsAsync<ResendPermanentException>(() =>
            client.SendAsync(Sample, CancellationToken.None));

        Assert.Equal((int)status, ex.HttpStatusCode);
        Assert.Equal("validation_error", ex.ResendErrorName);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ThrowsResendTransientException()
    {
        var (client, _) = BuildClient(_ => throw new HttpRequestException("dns unreachable"));

        var ex = await Assert.ThrowsAsync<ResendTransientException>(() =>
            client.SendAsync(Sample, CancellationToken.None));

        Assert.Contains("dns unreachable", ex.Message);
    }

    [Fact]
    public async Task SendAsync_HappyPath_SendsBearerAuthAndJsonBody()
    {
        HttpRequestMessage? captured = null;
        var (client, _) = BuildClient(req =>
        {
            captured = req;
            return BuildResponse(HttpStatusCode.OK, "{\"id\":\"abc\"}");
        });

        await client.SendAsync(Sample, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/emails", captured.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("re_test_key", captured.Headers.Authorization.Parameter);

        var bodyText = await captured.Content!.ReadAsStringAsync();
        Assert.Contains("\"to\":[\"user@example.com\"]", bodyText);
        Assert.Contains("\"subject\":\"Hello\"", bodyText);
        Assert.Contains("Skinora", bodyText); // FromAddress contains brand
    }

    [Fact]
    public async Task SendAsync_Success_WithMalformedBody_ThrowsTransient()
    {
        var (client, _) = BuildClient(_ =>
            BuildResponse(HttpStatusCode.OK, "this-is-not-json"));

        var ex = await Assert.ThrowsAsync<ResendTransientException>(() =>
            client.SendAsync(Sample, CancellationToken.None));

        Assert.Contains("could not be parsed", ex.Message);
    }

    [Fact]
    public async Task SendAsync_Success_WithMissingId_ThrowsTransient()
    {
        var (client, _) = BuildClient(_ =>
            BuildResponse(HttpStatusCode.OK, "{}"));

        var ex = await Assert.ThrowsAsync<ResendTransientException>(() =>
            client.SendAsync(Sample, CancellationToken.None));

        Assert.Contains("without an email id", ex.Message);
    }

    [Fact]
    public void Constructor_RejectsMissingApiKey()
    {
        var settings = new ResendSettings
        {
            ApiKey = string.Empty,
            FromAddress = "Skinora <noreply@skinora.com>",
        };

        var handler = new StubHttpMessageHandler(_ => BuildResponse(HttpStatusCode.OK, "{}"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com") };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ResendEmailClient(http, Options.Create(settings), NullLogger<ResendEmailClient>.Instance));

        Assert.Contains("ApiKey", ex.Message);
    }

    private static (ResendEmailClient Client, StubHttpMessageHandler Handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var settings = new ResendSettings
        {
            ApiKey = "re_test_key",
            BaseUrl = "https://api.resend.com",
            FromAddress = "Skinora <noreply@skinora.com>",
            TimeoutSeconds = 10,
        };

        var handler = new StubHttpMessageHandler(respond);
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.BaseUrl),
        };

        var client = new ResendEmailClient(http, Options.Create(settings), NullLogger<ResendEmailClient>.Instance);
        return (client, handler);
    }

    private static HttpResponseMessage BuildResponse(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
