namespace Skinora.Shared.Tests.Contract;

/// <summary>
/// Smoke tests verifying that ContractTestBase schema validation works correctly.
/// </summary>
public class ContractTestBaseSmokeTests : ContractTestBase
{
    private const string SampleSchema = """
        {
            "type": "object",
            "required": ["eventType", "timestamp", "payload"],
            "properties": {
                "eventType": { "type": "string" },
                "timestamp": { "type": "string", "format": "date-time" },
                "payload": { "type": "object" }
            },
            "additionalProperties": false
        }
        """;

    [Fact]
    public async Task ValidateAgainstSchema_ValidPayload_ReturnsNoErrors()
    {
        // Arrange
        var payload = new
        {
            eventType = "TradeOfferAccepted",
            timestamp = "2026-04-09T12:00:00Z",
            payload = new { tradeOfferId = "123" }
        };

        // Act
        var errors = await ValidateAgainstSchemaAsync(payload, SampleSchema);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAgainstSchema_MissingRequiredField_ReturnsErrors()
    {
        // Arrange — missing "timestamp" field
        var payload = new
        {
            eventType = "TradeOfferAccepted",
            payload = new { tradeOfferId = "123" }
        };

        // Act
        var errors = await ValidateAgainstSchemaAsync(payload, SampleSchema);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task AssertConformsToSchema_ValidPayload_DoesNotThrow()
    {
        // Arrange
        var payload = new
        {
            eventType = "PaymentDetected",
            timestamp = "2026-04-09T14:30:00Z",
            payload = new { amount = 100.50 }
        };

        // Act & Assert — should not throw
        await AssertConformsToSchemaAsync(payload, SampleSchema);
    }

    [Fact]
    public async Task AssertViolatesSchema_InvalidPayload_Succeeds()
    {
        // Arrange — extra field not allowed by additionalProperties: false
        var json = """{"eventType":"Test","timestamp":"2026-04-09T12:00:00Z","payload":{},"extra":"notAllowed"}""";

        // Act
        var errors = await ValidateJsonAgainstSchemaAsync(json, SampleSchema);

        // Assert
        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task GenerateSchemaFromType_ProducesValidSchema()
    {
        // Act
        var schema = await GenerateSchemaFromTypeAsync<SampleWebhookDto>();

        // Assert — NJsonSchema uses PascalCase by default (mirrors C# property names)
        Assert.NotNull(schema);
        var json = schema.ToJson();
        Assert.Contains("EventType", json);
        Assert.Contains("Timestamp", json);
    }

    // Sample DTO for schema generation test
    private class SampleWebhookDto
    {
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public object? Payload { get; set; }
    }
}
