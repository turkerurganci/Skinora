using System.Security.Cryptography;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Default <see cref="IInvitationCodeGenerator"/> backed by
/// <see cref="RandomNumberGenerator"/>. Emits 22-character base64url tokens
/// (16 random bytes → 128 bits of entropy) — enough to make brute-force
/// guessing impractical and short enough for a clean URL path.
/// </summary>
public sealed class InvitationCodeGenerator : IInvitationCodeGenerator
{
    private const int RandomByteCount = 16;

    public string Generate()
    {
        Span<byte> buffer = stackalloc byte[RandomByteCount];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var raw = Convert.ToBase64String(bytes);
        return raw
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
