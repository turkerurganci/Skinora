using System.Security.Cryptography;
using System.Text;
using Skinora.Shared.Discord;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Deterministic stub: derives a stable fake Discord user id +
/// username from the SHA-256 hash of the incoming authorization code,
/// so repeated calls with the same code produce the same profile. An
/// explicit <c>deny-*</c> prefix returns <c>null</c> so tests can
/// simulate the "user denied" branch (07 §5.13).
/// </summary>
/// <remarks>
/// <para>
/// Stays registered when <c>Discord:Provider</c> is anything other
/// than <c>discord</c> (T80 added the real
/// <see cref="DiscordOAuthClient"/> behind the same interface and
/// wires it via the composition root provider switch). CI + dev
/// environments keep using the stub so the test pipeline never
/// contacts Discord.
/// </para>
/// </remarks>
public sealed class StubDiscordOAuthClient : IDiscordOAuthClient
{
    public Task<DiscordProfile?> ExchangeAsync(
        string authorizationCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
            return Task.FromResult<DiscordProfile?>(null);

        if (authorizationCode.StartsWith("deny-", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<DiscordProfile?>(null);

        // T80 — let integration tests exercise the new InvalidGrant
        // branch of the callback redirect without standing up a real
        // OAuth client. "invalid-*" codes raise the exact exception the
        // real DiscordOAuthClient would for a Discord 400 invalid_grant.
        if (authorizationCode.StartsWith("invalid-", StringComparison.OrdinalIgnoreCase))
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.InvalidGrant,
                "Stub: simulated invalid_grant from Discord.",
                httpStatusCode: 400);
        }

        if (authorizationCode.StartsWith("transport-fail-", StringComparison.OrdinalIgnoreCase))
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.TokenExchangeFailed,
                "Stub: simulated transport failure.");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationCode));
        var discordUserId = ((ulong)BitConverter.ToInt64(hash, 0) & 0x0FFFFFFFFFFFFFFFul)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var discriminator = (BitConverter.ToUInt16(hash, 8) % 9999)
            .ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

        return Task.FromResult<DiscordProfile?>(
            new DiscordProfile(discordUserId, $"StubUser#{discriminator}"));
    }
}
