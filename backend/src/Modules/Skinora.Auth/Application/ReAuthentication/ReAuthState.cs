namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// State persisted in the re-verify cookie between A5 (initiate) and A6
/// (callback) — 07 §4.6–§4.7. Protected with <see cref="IReAuthStateProtector"/>
/// so the payload is tamper-evident and cannot be replayed after TTL expiry.
/// </summary>
/// <param name="UserId">Platform user who initiated the re-verify.</param>
/// <param name="SteamId">SteamID64 expected from the OpenID callback.</param>
/// <param name="ReturnUrl">Sanitized relative path to redirect to after issuance.</param>
/// <param name="IssuedAtUnix">Unix seconds (UTC) — TTL check in <see cref="ReAuthStateProtector"/>.</param>
public sealed record ReAuthState(Guid UserId, string SteamId, string ReturnUrl, long IssuedAtUnix);
