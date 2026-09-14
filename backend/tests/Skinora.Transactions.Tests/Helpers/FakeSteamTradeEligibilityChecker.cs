using Skinora.Transactions.Application.Lifecycle;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// Drivable <see cref="ISteamTradeEligibilityChecker"/> for the lifecycle gate
/// tests (08 §2.2a).
///
/// <para>
/// Defaults to <see cref="SteamTradeEligibilityStatus.Eligible"/> so the tests
/// written before this gate existed keep exercising what they were written to
/// exercise. That default is the OPPOSITE of the production stub, which answers
/// "could not ask": here the point is to hold one variable still, in production
/// the point is that a forgotten wiring must not open the gate.
/// </para>
///
/// <para>
/// Counts calls so a test can prove the expensive Steam round-trip was NOT made
/// when a cheaper rule already rejected the request — the same property the
/// trade-hold fakes in these files pin.
/// </para>
/// </summary>
public sealed class FakeSteamTradeEligibilityChecker : ISteamTradeEligibilityChecker
{
    private readonly SteamTradeEligibilityResult _result;

    public FakeSteamTradeEligibilityChecker(SteamTradeEligibilityResult? result = null)
    {
        _result = result ?? SteamTradeEligibilityResult.Eligible;
    }

    public int CallCount { get; private set; }

    public Guid? LastUserId { get; private set; }

    public Task<SteamTradeEligibilityResult> EvaluateAsync(User user, CancellationToken cancellationToken)
    {
        CallCount++;
        LastUserId = user.Id;
        return Task.FromResult(_result);
    }

    public static FakeSteamTradeEligibilityChecker Limited() =>
        new(SteamTradeEligibilityResult.Limited);

    public static FakeSteamTradeEligibilityChecker TooNew(int remainingDays = 7) =>
        new(SteamTradeEligibilityResult.TooNew(remainingDays));

    public static FakeSteamTradeEligibilityChecker Unknown() =>
        new(SteamTradeEligibilityResult.Unknown);
}
