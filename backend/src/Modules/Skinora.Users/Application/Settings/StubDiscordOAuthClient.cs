using System.Security.Cryptography;
using System.Text;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Deterministic stub: derives a stable fake Discord user id + username from
/// the SHA-256 hash of the incoming authorization code, so repeated calls
/// with the same code produce the same profile. An explicit
/// <c>deny-*</c> prefix returns <c>null</c> so tests can simulate the
/// "user denied" branch (07 §5.13).
/// </summary>
public sealed class StubDiscordOAuthClient : IDiscordOAuthClient
{
    public Task<DiscordProfile?> ExchangeAsync(
        string authorizationCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
            return Task.FromResult<DiscordProfile?>(null);

        if (authorizationCode.StartsWith("deny-", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<DiscordProfile?>(null);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationCode));
        var discordUserId = ((ulong)BitConverter.ToInt64(hash, 0) & 0x0FFFFFFFFFFFFFFFul)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var discriminator = (BitConverter.ToUInt16(hash, 8) % 9999)
            .ToString("D4", System.Globalization.CultureInfo.InvariantCulture);

        return Task.FromResult<DiscordProfile?>(
            new DiscordProfile(discordUserId, $"StubUser#{discriminator}"));
    }
}
