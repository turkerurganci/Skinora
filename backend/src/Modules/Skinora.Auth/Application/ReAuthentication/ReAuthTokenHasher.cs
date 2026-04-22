using System.Security.Cryptography;
using System.Text;

namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// SHA-256 hashing for re-verify tokens — mirrors
/// <see cref="Skinora.Auth.Application.SteamAuthentication.RefreshTokenGenerator"/>
/// hashing policy so the two flows stay auditable alike (hex, uppercase).
/// </summary>
internal static class ReAuthTokenHasher
{
    public static string Hash(string plainText)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));
}
