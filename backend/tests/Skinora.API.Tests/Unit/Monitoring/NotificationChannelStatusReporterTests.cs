using Microsoft.Extensions.Logging;
using Skinora.API.Monitoring;
using Skinora.Shared.Notifications;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T165 düzeltme turu (doğrulama bozması M26) — a real provider with missing
/// secrets is announced at <b>Error</b> level on startup (the operator's log
/// alarm and Loki filter on it), live / disabled channels at Information.
/// Lowering the misconfigured line to Information went unnoticed before.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationChannelStatusReporterTests
{
    [Fact]
    public void MisconfiguredChannel_IsLoggedAsError_WithKeyNamesOnly()
    {
        var channels = NotificationChannelAvailability.FromConfiguration(key => key switch
        {
            "Telegram:Provider" => "telegram",
            "Discord:Provider" => "disabled",
            _ => null,
        });
        var logger = new RecordingLogger();

        NotificationChannelStatusReporter.Report(channels, isDevelopment: false, logger);

        var telegram = Assert.Single(logger.Entries, e => e.Message.Contains("TELEGRAM", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, telegram.Level);
        Assert.Contains("MISCONFIGURED", telegram.Message, StringComparison.Ordinal);
        Assert.Contains("Telegram:BotToken (missing)", telegram.Message, StringComparison.Ordinal);

        var discord = Assert.Single(logger.Entries, e => e.Message.Contains("DISCORD", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, discord.Level);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error && !e.Message.Contains("TELEGRAM", StringComparison.Ordinal));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
