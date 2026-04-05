using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

public class MiddlewarePipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MiddlewarePipelineTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace DbContext with in-memory SQLite for tests
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:"));
            });
        }).CreateClient();
    }

    #region ExceptionHandlingMiddleware Tests

    [Fact]
    public async Task NotFoundException_Returns404_WithErrorEnvelope()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/not-found");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(body.Success);
        Assert.Null(body.Data);
        Assert.NotNull(body.Error);
        Assert.Equal("NOT_FOUND", body.Error!.Code);
        Assert.NotNull(body.TraceId);
    }

    [Fact]
    public async Task BusinessRuleException_Returns422_WithErrorCode()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/business-rule");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(body.Success);
        Assert.Equal("ACTIVE_TRANSACTION_EXISTS", body.Error!.Code);
    }

    [Fact]
    public async Task DomainException_Returns409_WithErrorCode()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/domain");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(body.Success);
        Assert.Equal("INVALID_STATE_TRANSITION", body.Error!.Code);
    }

    [Fact]
    public async Task IntegrationException_Returns502()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/integration");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.False(body.Success);
        Assert.Equal("INTEGRATION_ERROR", body.Error!.Code);
    }

    [Fact]
    public async Task UnhandledException_Returns500_WithGenericMessage()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/unhandled");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(body.Success);
        Assert.Equal("INTERNAL_ERROR", body.Error!.Code);
        Assert.Equal("An unexpected error occurred.", body.Error.Message);
    }

    [Fact]
    public async Task ErrorResponse_ContainsTraceId()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/not-found");
        var body = await DeserializeResponse(response);

        Assert.NotNull(body.TraceId);
        Assert.NotEmpty(body.TraceId!);
    }

    #endregion

    #region CorrelationIdMiddleware Tests

    [Fact]
    public async Task CorrelationId_GeneratedWhenNotProvided()
    {
        var response = await _client.GetAsync("/api/v1/_diag/ping");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var correlationId = response.Headers.GetValues("X-Correlation-Id").First();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    [Fact]
    public async Task CorrelationId_PreservedWhenProvided()
    {
        var requestCorrelationId = "test-correlation-id-12345";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_diag/ping");
        request.Headers.Add("X-Correlation-Id", requestCorrelationId);

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var responseCorrelationId = response.Headers.GetValues("X-Correlation-Id").First();
        Assert.Equal(requestCorrelationId, responseCorrelationId);
    }

    [Fact]
    public async Task CorrelationId_PresentOnErrorResponses()
    {
        var response = await _client.GetAsync("/api/v1/_diag/throw/not-found");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    #endregion

    #region ApiResponseWrapperFilter Tests

    [Fact]
    public async Task SuccessResponse_WrappedInApiResponseEnvelope()
    {
        var response = await _client.GetAsync("/api/v1/_diag/ping");
        var body = await DeserializeResponse(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.Null(body.Error);
        Assert.NotNull(body.TraceId);
    }

    #endregion

    #region SecurityHeaders Tests

    [Fact]
    public async Task SecurityHeaders_Present()
    {
        var response = await _client.GetAsync("/api/v1/_diag/ping");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
    }

    [Fact]
    public async Task CSPHeader_Present()
    {
        var response = await _client.GetAsync("/api/v1/_diag/ping");

        Assert.True(response.Content.Headers.Contains("Content-Security-Policy")
                    || response.Headers.Contains("Content-Security-Policy"));
    }

    #endregion

    #region Pipeline Order Tests

    [Fact]
    public async Task HealthEndpoint_StillWorks()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Helpers

    private static async Task<TestApiResponse> DeserializeResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestApiResponse>(content, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize response: {content}");
    }

    private record TestApiResponse(
        bool Success,
        JsonElement? Data,
        TestApiError? Error,
        string? TraceId);

    private record TestApiError(
        string Code,
        string Message,
        JsonElement? Details);

    #endregion
}
