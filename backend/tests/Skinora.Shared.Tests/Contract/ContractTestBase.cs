using System.Text.Json;
using NJsonSchema;

namespace Skinora.Shared.Tests.Contract;

/// <summary>
/// Base class for contract tests that validate sidecar-backend HTTP/webhook payload schemas.
/// Uses NJsonSchema for JSON schema validation per 09 §12.7.
/// </summary>
public abstract class ContractTestBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Validates a payload object against a JSON schema string.
    /// Returns validation errors; empty collection means the payload conforms.
    /// </summary>
    protected static async Task<ICollection<NJsonSchema.Validation.ValidationError>> ValidateAgainstSchemaAsync(
        object payload,
        string jsonSchema)
    {
        var schema = await JsonSchema.FromJsonAsync(jsonSchema);
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return schema.Validate(json);
    }

    /// <summary>
    /// Validates a raw JSON string against a JSON schema string.
    /// </summary>
    protected static async Task<ICollection<NJsonSchema.Validation.ValidationError>> ValidateJsonAgainstSchemaAsync(
        string json,
        string jsonSchema)
    {
        var schema = await JsonSchema.FromJsonAsync(jsonSchema);
        return schema.Validate(json);
    }

    /// <summary>
    /// Loads a JSON schema from an embedded resource or file path under the schemas directory.
    /// Subclasses should place schema files in a "Schemas" folder and set them as EmbeddedResource
    /// or use <see cref="LoadSchemaFromFileAsync"/> for file-based schemas.
    /// </summary>
    protected static async Task<string> LoadSchemaFromFileAsync(string schemaFilePath)
    {
        return await File.ReadAllTextAsync(schemaFilePath);
    }

    /// <summary>
    /// Generates a JSON schema from a .NET type. Useful for verifying that
    /// the backend DTO structure matches the expected sidecar contract.
    /// </summary>
    protected static async Task<JsonSchema> GenerateSchemaFromTypeAsync<T>()
    {
        return await Task.FromResult(JsonSchema.FromType<T>());
    }

    /// <summary>
    /// Asserts that a payload conforms to the given JSON schema.
    /// Throws <see cref="Xunit.Sdk.XunitException"/> with details if validation fails.
    /// </summary>
    protected static async Task AssertConformsToSchemaAsync(object payload, string jsonSchema)
    {
        var errors = await ValidateAgainstSchemaAsync(payload, jsonSchema);

        if (errors.Count > 0)
        {
            var errorMessages = errors.Select(e =>
                $"  - {e.Path}: {e.Kind} ({e.Property})");
            Assert.Fail(
                $"Payload does not conform to schema. Errors:\n{string.Join("\n", errorMessages)}");
        }
    }

    /// <summary>
    /// Asserts that a payload does NOT conform to the schema (negative testing).
    /// </summary>
    protected static async Task AssertViolatesSchemaAsync(object payload, string jsonSchema)
    {
        var errors = await ValidateAgainstSchemaAsync(payload, jsonSchema);
        Assert.True(errors.Count > 0, "Expected schema validation errors but payload conforms.");
    }
}
