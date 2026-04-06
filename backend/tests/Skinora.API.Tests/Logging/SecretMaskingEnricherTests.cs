using Serilog;
using Serilog.Events;
using Skinora.API.Logging;

namespace Skinora.API.Tests.Logging;

public class SecretMaskingEnricherTests
{
    private static LogEvent CaptureLogEvent(Action<ILogger> logAction)
    {
        LogEvent? captured = null;
        var logger = new LoggerConfiguration()
            .Enrich.With<SecretMaskingEnricher>()
            .WriteTo.Sink(new DelegatingSink(e => captured = e))
            .CreateLogger();

        logAction(logger);
        logger.Dispose();
        Assert.NotNull(captured);
        return captured!;
    }

    [Theory]
    [InlineData("privateKey")]
    [InlineData("apiKey")]
    [InlineData("refreshToken")]
    [InlineData("accessToken")]
    [InlineData("password")]
    [InlineData("secret")]
    [InlineData("jwtSecret")]
    [InlineData("mnemonic")]
    [InlineData("hdWalletMnemonic")]
    [InlineData("authorization")]
    public void Enrich_FullyMaskedField_ReplacesValueWithMask(string fieldName)
    {
        var template = "Sensitive {" + fieldName + "}";
        var logEvent = CaptureLogEvent(log => log.Information(template, "actual-secret-value"));

        var prop = logEvent.Properties[fieldName];
        Assert.Equal("\"***\"", prop.ToString());
    }

    [Fact]
    public void Enrich_WalletAddress_PartiallyMasksKeepingPrefixAndSuffix()
    {
        var logEvent = CaptureLogEvent(log => log.Information("Wallet {walletAddress}", "TXyz1234abcdMiddlePart9876"));

        var prop = (ScalarValue)logEvent.Properties["walletAddress"];
        Assert.Equal("TXyz…9876", prop.Value);
    }

    [Fact]
    public void Enrich_ShortWalletAddress_FullyMasked()
    {
        var logEvent = CaptureLogEvent(log => log.Information("Wallet {walletAddress}", "TXyz12"));

        var prop = (ScalarValue)logEvent.Properties["walletAddress"];
        Assert.Equal("***", prop.Value);
    }

    [Fact]
    public void Enrich_NonSensitiveField_LeftUntouched()
    {
        var logEvent = CaptureLogEvent(log => log.Information("Order {orderId}", "ord-12345"));

        var prop = (ScalarValue)logEvent.Properties["orderId"];
        Assert.Equal("ord-12345", prop.Value);
    }

    [Fact]
    public void Enrich_NestedStructure_MasksSecretInInnerProperty()
    {
        var payload = new { userId = "u-1", apiKey = "leaked-key" };
        var logEvent = CaptureLogEvent(log => log.Information("Payload {@payload}", payload));

        var structure = (StructureValue)logEvent.Properties["payload"];
        var apiKeyProp = structure.Properties.Single(p => p.Name == "apiKey");
        Assert.Equal("\"***\"", apiKeyProp.Value.ToString());

        var userIdProp = structure.Properties.Single(p => p.Name == "userId");
        Assert.Equal("u-1", ((ScalarValue)userIdProp.Value).Value);
    }

    [Fact]
    public void Enrich_FieldNameMatching_IsCaseInsensitive()
    {
        var logEvent = CaptureLogEvent(log => log.Information("Mixed {ApiKey}", "leaked"));

        var prop = logEvent.Properties["ApiKey"];
        Assert.Equal("\"***\"", prop.ToString());
    }

    private sealed class DelegatingSink : Serilog.Core.ILogEventSink
    {
        private readonly Action<LogEvent> _onEmit;

        public DelegatingSink(Action<LogEvent> onEmit) => _onEmit = onEmit;

        public void Emit(LogEvent logEvent) => _onEmit(logEvent);
    }
}
