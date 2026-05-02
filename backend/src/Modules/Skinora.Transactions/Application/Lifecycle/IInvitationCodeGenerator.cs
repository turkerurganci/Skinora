namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Generates the opaque, single-use <c>InviteToken</c> (06 §3.5) used by the
/// buyer-invite link emitted in <c>POST /transactions</c> response and by
/// <c>GET /transactions/:id</c> for unauthenticated lookups (07 §7.5 will
/// consume this in T46). Tokens are URL-safe and have ≥128 bits of entropy.
/// </summary>
public interface IInvitationCodeGenerator
{
    string Generate();
}
