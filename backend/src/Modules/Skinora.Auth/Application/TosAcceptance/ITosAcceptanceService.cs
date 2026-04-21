namespace Skinora.Auth.Application.TosAcceptance;

/// <summary>
/// Captures Terms of Service acceptance and 18+ age self-attestation
/// (07 §4.4, 02 §21.1, 03 §11a.2). Idempotent only in the sense that a
/// re-acceptance is rejected with 409 <c>TOS_ALREADY_ACCEPTED</c> — the
/// pipeline treats the first acceptance as final.
/// </summary>
public interface ITosAcceptanceService
{
    Task<TosAcceptanceResult> AcceptAsync(
        Guid userId, string tosVersion, bool ageOver18, CancellationToken cancellationToken);
}

public sealed record TosAcceptanceResult(DateTime AcceptedAt);
