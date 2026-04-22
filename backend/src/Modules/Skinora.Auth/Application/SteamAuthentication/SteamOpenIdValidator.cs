using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Steam OpenID 2.0 <c>check_authentication</c> verifier — 08 §2.1.
/// Rewrites the callback parameters with <c>openid.mode=check_authentication</c>
/// and POSTs them back to Steam's login endpoint. The response is parsed as
/// key=value pairs; <c>is_valid:true</c> means the assertion is genuine.
/// </summary>
public sealed class SteamOpenIdValidator : ISteamOpenIdValidator
{
    private readonly HttpClient _httpClient;
    private readonly SteamOpenIdSettings _settings;
    private readonly ILogger<SteamOpenIdValidator> _logger;

    public SteamOpenIdValidator(
        HttpClient httpClient,
        IOptions<SteamOpenIdSettings> settings,
        ILogger<SteamOpenIdValidator> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<SteamOpenIdValidationResult> ValidateAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        CancellationToken cancellationToken)
        => ValidateAsync(callbackParameters, _settings.ReturnToUrl, cancellationToken);

    public async Task<SteamOpenIdValidationResult> ValidateAsync(
        IReadOnlyDictionary<string, string> callbackParameters,
        string expectedReturnTo,
        CancellationToken cancellationToken)
    {
        if (!callbackParameters.TryGetValue("openid.mode", out var mode) || mode != "id_res")
            return SteamOpenIdValidationResult.Failure("openid.mode is not id_res");

        if (!callbackParameters.TryGetValue("openid.return_to", out var returnTo)
            || !string.Equals(returnTo, expectedReturnTo, StringComparison.Ordinal))
        {
            return SteamOpenIdValidationResult.Failure("openid.return_to mismatch");
        }

        if (!callbackParameters.TryGetValue("openid.claimed_id", out var claimedId))
            return SteamOpenIdValidationResult.Failure("openid.claimed_id missing");

        var body = callbackParameters.ToDictionary(kv => kv.Key, kv => kv.Value);
        body["openid.mode"] = "check_authentication";

        using var content = new FormUrlEncodedContent(body);
        using var response = await _httpClient.PostAsync(
            SteamOpenIdUrlBuilder.LoginEndpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Steam OpenID check_authentication returned non-success status {StatusCode}",
                (int)response.StatusCode);
            return SteamOpenIdValidationResult.Failure(
                $"check_authentication HTTP {(int)response.StatusCode}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!IsAssertionValid(responseBody))
            return SteamOpenIdValidationResult.Failure("Steam response is_valid:false");

        if (!SteamIdParser.TryParse(claimedId, out var steamId64))
            return SteamOpenIdValidationResult.Failure("claimed_id does not carry a SteamID64");

        return SteamOpenIdValidationResult.Success(steamId64);
    }

    private static bool IsAssertionValid(string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("is_valid:", StringComparison.Ordinal))
                return line.Equals("is_valid:true", StringComparison.Ordinal);
        }
        return false;
    }
}
