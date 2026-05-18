using System.Security.Cryptography;
using System.Text;
using Skinora.Shared.Email;

namespace Skinora.Shared.Tests.Unit.Email;

/// <summary>
/// Unit coverage for <see cref="SvixSignatureVerifier"/> (T78).
/// Validates the documented Svix algorithm: base64-decoded
/// <c>whsec_</c>-prefixed secret, HMAC-SHA256 over
/// <c>"{svix-id}.{svix-timestamp}.{body}"</c>, base64 signature,
/// space-delimited <c>v1,...</c> header, ±5 minute replay window.
/// </summary>
public sealed class SvixSignatureVerifierTests
{
    private static readonly DateTime Anchor = new(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Verify_ValidV1Signature_ReturnsValid()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);
        var msgId = "msg_2abc";
        var unix = AnchorUnix();
        var body = "{\"type\":\"email.bounced\",\"data\":{}}";
        var sig = "v1," + ComputeSignature(key, msgId, unix, body);

        var verifier = new SvixSignatureVerifier();

        var result = verifier.Verify(secret, msgId, unix, sig, body, 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.Valid, result);
    }

    [Fact]
    public void Verify_MultipleSpaceDelimitedVersions_AcceptsAnyV1()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);
        var msgId = "msg_2abc";
        var unix = AnchorUnix();
        var body = "{}";
        var validV1 = "v1," + ComputeSignature(key, msgId, unix, body);

        // v2 first, then a junk v1, then a valid v1 — all space-delimited.
        var sig = "v2,zzz " + "v1,bogus " + validV1;

        var verifier = new SvixSignatureVerifier();
        var result = verifier.Verify(secret, msgId, unix, sig, body, 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.Valid, result);
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsSignatureMismatch()
    {
        var trueKey = RandomKey();
        var fakeKey = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(fakeKey);
        var msgId = "msg_2abc";
        var unix = AnchorUnix();
        var body = "{}";
        var sig = "v1," + ComputeSignature(trueKey, msgId, unix, body);

        var verifier = new SvixSignatureVerifier();
        var result = verifier.Verify(secret, msgId, unix, sig, body, 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.SignatureMismatch, result);
    }

    [Fact]
    public void Verify_BodyTamper_ReturnsSignatureMismatch()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);
        var msgId = "msg_2abc";
        var unix = AnchorUnix();
        var originalBody = "{\"type\":\"email.bounced\"}";
        var sig = "v1," + ComputeSignature(key, msgId, unix, originalBody);

        var verifier = new SvixSignatureVerifier();
        var result = verifier.Verify(secret, msgId, unix, sig, originalBody + "tampered", 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.SignatureMismatch, result);
    }

    [Fact]
    public void Verify_TimestampPastWindow_ReturnsOutOfWindow()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);
        var msgId = "msg_2abc";
        var staleUnix = ((DateTimeOffset)Anchor.AddMinutes(-10)).ToUnixTimeSeconds().ToString();
        var body = "{}";
        var sig = "v1," + ComputeSignature(key, msgId, staleUnix, body);

        var verifier = new SvixSignatureVerifier();
        var result = verifier.Verify(secret, msgId, staleUnix, sig, body, 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.TimestampOutOfWindow, result);
    }

    [Fact]
    public void Verify_MissingSecretPrefix_ReturnsInvalidSecretConfiguration()
    {
        var verifier = new SvixSignatureVerifier();

        var result = verifier.Verify(
            "missing-prefix-secret",
            "msg_2abc",
            AnchorUnix(),
            "v1,zzz",
            "{}",
            300,
            Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.InvalidSecretConfiguration, result);
    }

    [Fact]
    public void Verify_MissingHeaders_ReturnsMissingHeaders()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);

        var verifier = new SvixSignatureVerifier();

        var result = verifier.Verify(secret, null, null, null, "{}", 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.MissingHeaders, result);
    }

    [Fact]
    public void Verify_NonNumericTimestamp_ReturnsTimestampUnparseable()
    {
        var key = RandomKey();
        var secret = "whsec_" + Convert.ToBase64String(key);

        var verifier = new SvixSignatureVerifier();

        var result = verifier.Verify(secret, "msg_1", "not-a-number", "v1,zzz", "{}", 300, Anchor);

        Assert.Equal(SvixSignatureVerifier.VerifyResult.TimestampUnparseable, result);
    }

    private static string AnchorUnix()
        => ((DateTimeOffset)Anchor).ToUnixTimeSeconds().ToString();

    private static string ComputeSignature(byte[] key, string msgId, string unix, string body)
    {
        var signed = $"{msgId}.{unix}.{body}";
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed));
        return Convert.ToBase64String(hash);
    }

    private static byte[] RandomKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }
}
