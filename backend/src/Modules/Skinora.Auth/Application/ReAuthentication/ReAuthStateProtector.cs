using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Default <see cref="IReAuthStateProtector"/> — uses ASP.NET Core Data Protection
/// with a 10-minute TTL window (long enough for the user to complete Steam's
/// OpenID consent, short enough to keep replay risk negligible). The TTL is
/// enforced both via <c>ProtectWithLifetime</c> and a secondary
/// <see cref="ReAuthState.IssuedAtUnix"/> comparison to catch clock drift.
/// </summary>
public sealed class ReAuthStateProtector : IReAuthStateProtector
{
    public const string ProtectorPurpose = "Skinora.Auth.ReAuth.State";
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public ReAuthStateProtector(IDataProtectionProvider provider, TimeProvider timeProvider)
    {
        _protector = provider.CreateProtector(ProtectorPurpose).ToTimeLimitedDataProtector();
        _timeProvider = timeProvider;
    }

    public string Protect(ReAuthState state)
    {
        var json = JsonSerializer.Serialize(state);
        return _protector.Protect(json, StateLifetime);
    }

    public ReAuthState? Unprotect(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return null;

        string json;
        try
        {
            json = _protector.Unprotect(cipherText);
        }
        catch
        {
            return null;
        }

        ReAuthState? state;
        try
        {
            state = JsonSerializer.Deserialize<ReAuthState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (state is null) return null;

        var ageSeconds = _timeProvider.GetUtcNow().ToUnixTimeSeconds() - state.IssuedAtUnix;
        if (ageSeconds < 0 || ageSeconds > (long)StateLifetime.TotalSeconds) return null;

        return state;
    }
}
