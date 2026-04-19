using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Tests.Integration;

/// <summary>
/// Smoke tests verifying that IntegrationTestBase correctly spins up a SQL Server
/// container via TestContainers and applies EF Core schema creation.
/// </summary>
public class IntegrationTestBaseSmokeTests : IntegrationTestBase
{
    [Fact]
    public void ConnectionString_IsNotEmpty_AfterInitialization()
    {
        // Assert
        Assert.False(string.IsNullOrWhiteSpace(ConnectionString));
        // SqlConnectionStringBuilder canonicalises "Server=" to "Data Source=".
        // Accept either so the assertion survives T11.3's shared-server refactor
        // without coupling to a specific key name.
        Assert.True(
            ConnectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            ConnectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase),
            $"Expected connection string to identify the server; got: {ConnectionString}");
        Assert.Contains("Initial Catalog=T_", ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_CanExecuteRawQuery()
    {
        // Arrange & Act
        var result = await Context.Database.ExecuteSqlRawAsync("SELECT 1");

        // Assert — ExecuteSqlRawAsync returns rows affected; SELECT 1 returns -1
        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task OutboxMessages_TableExists_AndAcceptsInsert()
    {
        // Arrange
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        Context.OutboxMessages.Add(message);
        await Context.SaveChangesAsync();

        // Assert
        var loaded = await Context.OutboxMessages.FirstAsync(m => m.Id == message.Id);
        Assert.Equal("TestEvent", loaded.EventType);
    }

    [Fact]
    public void CreateContext_ReturnsNewInstance()
    {
        // Arrange & Act
        using var secondContext = CreateContext();

        // Assert — different instances pointing at same database
        Assert.NotSame(Context, secondContext);
    }
}
