namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Soft age gate — rejects logins when the Steam account was created less
/// than <c>auth.min_steam_account_age_days</c> days ago. MVP heuristic per
/// 02 §21.1 and 03 §11a.2: burner/fake account deterrent, not a biological
/// age check. Combined with the 18+ self-attestation captured at ToS accept
/// (07 §4.4), this is the full MVP age gate.
/// </summary>
public interface IAgeGateCheck
{
    Task<AgeGateDecision> EvaluateAsync(
        DateTime? steamAccountCreatedAt, CancellationToken cancellationToken);
}

public sealed record AgeGateDecision(bool IsBlocked, int? AccountAgeDays, int? RequiredDays)
{
    public static AgeGateDecision Allowed() => new(false, null, null);
    public static AgeGateDecision Blocked(int accountAgeDays, int requiredDays)
        => new(true, accountAgeDays, requiredDays);
}
