namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Validates the <c>returnUrl</c> query parameter on <c>GET /auth/steam</c>
/// per 07 §4.2 — only relative paths allowed (open-redirect protection).
/// </summary>
public interface IReturnUrlValidator
{
    /// <summary>Returns the sanitized path, or the configured default when the input is rejected.</summary>
    string Sanitize(string? returnUrl);
}

public sealed class ReturnUrlValidator : IReturnUrlValidator
{
    private readonly string _defaultPath;

    public ReturnUrlValidator(string defaultPath)
    {
        _defaultPath = defaultPath;
    }

    public string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return _defaultPath;

        // Reject protocol-relative (//evil.com) and absolute URLs.
        if (returnUrl.StartsWith("//", StringComparison.Ordinal))
            return _defaultPath;

        if (returnUrl.Contains("://", StringComparison.Ordinal))
            return _defaultPath;

        // Reject backslash variants used to bypass naive checks (\\evil.com).
        if (returnUrl.StartsWith("\\", StringComparison.Ordinal))
            return _defaultPath;

        // Must be a single-segment relative path starting with "/".
        if (!returnUrl.StartsWith('/'))
            return _defaultPath;

        return returnUrl;
    }
}
