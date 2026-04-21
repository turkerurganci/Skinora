namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Pipeline hook for sanctions screening — the real implementation lands in
/// T82 (OFAC/EU list integration). <see cref="NoMatchSanctionsCheck"/> is
/// the default no-op used until then.
/// </summary>
public interface ISanctionsCheck
{
    Task<SanctionsDecision> EvaluateAsync(string steamId64, CancellationToken cancellationToken);
}

public sealed record SanctionsDecision(bool IsMatch, string? MatchedList)
{
    public static SanctionsDecision NoMatch() => new(false, null);
    public static SanctionsDecision Match(string list) => new(true, list);
}

public sealed class NoMatchSanctionsCheck : ISanctionsCheck
{
    public Task<SanctionsDecision> EvaluateAsync(string steamId64, CancellationToken cancellationToken)
        => Task.FromResult(SanctionsDecision.NoMatch());
}
