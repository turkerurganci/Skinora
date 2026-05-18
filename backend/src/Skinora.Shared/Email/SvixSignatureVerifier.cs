using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Skinora.Shared.Email;

/// <summary>
/// Verifier for Svix-style webhook signatures (Resend uses Svix; the
/// algorithm is documented at <c>docs.svix.com/receiving/verifying-payloads/how-manual</c>).
/// Used by the <c>POST /api/v1/webhooks/resend</c> filter to authenticate
/// inbound bounce / complaint / delivery-delayed callbacks (08 §4.3).
/// </summary>
/// <remarks>
/// <para>
/// Algorithm:
/// </para>
/// <list type="number">
///   <item>Secret arrives as <c>whsec_BASE64...</c>; strip the prefix and
///         base64-decode the rest to obtain the HMAC key.</item>
///   <item>Signed content is the literal string <c>{msg_id}.{timestamp}.{body}</c>
///         — no JSON re-serialization. The raw request body must be
///         preserved byte-for-byte upstream of this call.</item>
///   <item>Compute <c>HMAC-SHA256(key, signedContent)</c> and base64-encode
///         the result.</item>
///   <item>The <c>svix-signature</c> header is a space-delimited list of
///         <c>version,base64</c> pairs (e.g. <c>v1,abc... v2,def...</c>).
///         Accept the request if any <c>v1</c> entry matches the computed
///         value under a constant-time compare.</item>
///   <item>Reject when the parsed <c>svix-timestamp</c> drifts more than
///         <c>replayWindowSeconds</c> from <c>UtcNow</c>.</item>
/// </list>
/// </remarks>
public sealed class SvixSignatureVerifier
{
    public const string WebhookSecretPrefix = "whsec_";

    public enum VerifyResult
    {
        Valid,
        InvalidSecretConfiguration,
        MissingHeaders,
        TimestampUnparseable,
        TimestampOutOfWindow,
        SignatureMismatch,
    }

    /// <summary>
    /// Verify a Svix-style webhook payload. Returns
    /// <see cref="VerifyResult.Valid"/> only when every check passes; any
    /// failure returns the specific reason so the caller can log /
    /// 401-respond with structured context.
    /// </summary>
    public VerifyResult Verify(
        string? webhookSigningSecret,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        string rawBody,
        int replayWindowSeconds,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(webhookSigningSecret)
            || !webhookSigningSecret.StartsWith(WebhookSecretPrefix, StringComparison.Ordinal))
        {
            return VerifyResult.InvalidSecretConfiguration;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(webhookSigningSecret[WebhookSecretPrefix.Length..]);
        }
        catch (FormatException)
        {
            return VerifyResult.InvalidSecretConfiguration;
        }

        if (string.IsNullOrWhiteSpace(svixId)
            || string.IsNullOrWhiteSpace(svixTimestamp)
            || string.IsNullOrWhiteSpace(svixSignature))
        {
            return VerifyResult.MissingHeaders;
        }

        if (!long.TryParse(svixTimestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return VerifyResult.TimestampUnparseable;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        var skewSeconds = Math.Abs((utcNow - sentAt).TotalSeconds);
        if (skewSeconds > replayWindowSeconds)
        {
            return VerifyResult.TimestampOutOfWindow;
        }

        var signedContent = $"{svixId}.{svixTimestamp}.{rawBody}";
        var computed = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedContent));
        var computedBase64 = Convert.ToBase64String(computed);

        // svix-signature: "v1,abc... v2,def... v1,zzz..." — accept if any
        // v1 entry matches. Other versions are ignored (forward-compat).
        foreach (var entry in svixSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(',', StringComparison.Ordinal);
            if (separator <= 0 || separator >= entry.Length - 1)
            {
                continue;
            }

            var version = entry[..separator];
            if (!string.Equals(version, "v1", StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = entry[(separator + 1)..];
            if (FixedTimeBase64Equals(computedBase64, candidate))
            {
                return VerifyResult.Valid;
            }
        }

        return VerifyResult.SignatureMismatch;
    }

    private static bool FixedTimeBase64Equals(string expected, string supplied)
    {
        if (expected.Length != supplied.Length)
        {
            // Length difference itself is not constant-time, but the
            // bigger leak (which byte differs) is still avoided.
            return false;
        }

        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var suppliedBytes = Encoding.ASCII.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
