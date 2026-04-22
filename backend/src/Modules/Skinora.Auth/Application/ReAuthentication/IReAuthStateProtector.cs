namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Tamper-evident encoder for the re-verify state cookie (07 §4.6). Wraps
/// ASP.NET Core <c>IDataProtector</c> so the concrete protector purpose and
/// TTL live in one place; callers handle only opaque strings.
/// </summary>
public interface IReAuthStateProtector
{
    /// <summary>Encodes the state as an opaque, signed, time-bound string.</summary>
    string Protect(ReAuthState state);

    /// <summary>Decodes the string or returns <c>null</c> when absent, tampered, or expired.</summary>
    ReAuthState? Unprotect(string? cipherText);
}
