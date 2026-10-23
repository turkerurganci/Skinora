using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// T172a doğrulama F1 (⚠ PARA) — writes the effective deposit index floor of
/// every network once when the backend starts, so the operator can check it
/// right after a restore (DEPLOY_RUNBOOK §N.8 step 3) instead of waiting for
/// the first new address. Reading the options also validates them: a value
/// <see cref="PaymentAddressIndexFloorOptions.Errors"/> refuses stops the
/// start (a second layer behind <c>ValidateOnStart</c>).
/// </summary>
public sealed class PaymentAddressIndexFloorStartupLog : IHostedService
{
    private readonly IOptions<PaymentAddressIndexFloorOptions> _options;
    private readonly ILogger<PaymentAddressIndexFloorStartupLog> _logger;

    public PaymentAddressIndexFloorStartupLog(
        IOptions<PaymentAddressIndexFloorOptions> options,
        ILogger<PaymentAddressIndexFloorStartupLog> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>The summary the start-up line carries, e.g. <c>TRON = 5000, ETHEREUM = none</c>.</summary>
    public static string Describe(PaymentAddressIndexFloorOptions options) =>
        string.Join(", ", Enum.GetValues<BlockchainNetwork>().Select(network =>
        {
            var floor = options.FloorFor(network);
            return $"{network} = {(floor > 0 ? floor.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}";
        }));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var anyFloor = Enum.GetValues<BlockchainNetwork>().Any(n => options.FloorFor(n) > 0);
        _logger.Log(
            anyFloor ? LogLevel.Warning : LogLevel.Information,
            "Deposit index floor at start-up: {Floors} (PaymentAddresses:MinimumNextIndex — DEPLOY_RUNBOOK §N.8).",
            Describe(options));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
