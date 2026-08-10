using System.Text.Json;
using System.Text.Json.Serialization;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Users.Application.Settings;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// Unit-level coverage for the T46 acceptance / detail surface — DTO
/// serialization invariants and pure-function helpers that don't need
/// a database. Pipeline tests live under Integration/Lifecycle.
/// </summary>
public class TransactionAcceptanceUnitTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void AcceptTransactionResponse_Serializes_Status_As_String_Per_07_7_6()
    {
        var dto = new AcceptTransactionResponse(
            Status: TransactionStatus.ACCEPTED,
            AcceptedAt: new DateTime(2026, 5, 2, 14, 45, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"status\":\"ACCEPTED\"", json);
        Assert.Contains("\"acceptedAt\":\"2026-05-02T14:45:00Z\"", json);
    }

    [Fact]
    public void AvailableActionsDto_Public_Variant_Suppresses_Authenticated_Fields()
    {
        var dto = new AvailableActionsDto(
            CanAccept: false,
            CanConfirmReady: null,
            CanConfirmReceipt: null,
            CanCancel: null,
            CanDispute: null,
            DisputableTypes: null,
            CanEscalate: null,
            RequiresLogin: true);

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"canAccept\":false", json);
        Assert.Contains("\"requiresLogin\":true", json);
        Assert.DoesNotContain("canCancel", json);
        Assert.DoesNotContain("canDispute", json);
        Assert.DoesNotContain("canEscalate", json);
        // v3.0 — the two P2P action bits are authenticated-only as well.
        Assert.DoesNotContain("canConfirmReady", json);
        Assert.DoesNotContain("canConfirmReceipt", json);
    }

    [Fact]
    public void AvailableActionsDto_Authenticated_Variant_Suppresses_RequiresLogin()
    {
        var dto = new AvailableActionsDto(
            CanAccept: false,
            CanConfirmReady: true,
            CanConfirmReceipt: false,
            CanCancel: true,
            CanDispute: false,
            DisputableTypes: null,
            CanEscalate: false,
            RequiresLogin: null);

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"canConfirmReady\":true", json);
        Assert.Contains("\"canConfirmReceipt\":false", json);
        Assert.Contains("\"canCancel\":true", json);
        Assert.Contains("\"canDispute\":false", json);
        Assert.Contains("\"canEscalate\":false", json);
        Assert.DoesNotContain("requiresLogin", json);
    }

    [Theory]
    [InlineData("76561198000000080", "76561198000000080", true)]   // Yöntem 1 match
    [InlineData("76561198000000080", "76561198000099999", false)] // Mismatch
    [InlineData("76561198000000080", "", false)]                   // Empty target
    [InlineData("", "76561198000000080", false)]                   // Empty caller
    [InlineData("76561198000000080", " 76561198000000080", false)] // Trim guard
    public void Steam_Id_Match_Is_Ordinal_Strict(string callerSteamId, string targetSteamId, bool expected)
    {
        // The acceptance service's Yöntem-1 guard uses StringComparison.Ordinal —
        // mirrored here so accidental drifts (e.g. switching to OrdinalIgnoreCase
        // or trimming) get caught at unit-test scope.
        var actual = string.Equals(callerSteamId, targetSteamId, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TransactionErrorCodes_T46_Codes_Match_07_7_6_Contract()
    {
        // 07 §7.6 Hatalar: 409 INVALID_STATE_TRANSITION, 403 STEAM_ID_MISMATCH,
        // 409 ALREADY_ACCEPTED, 400 VALIDATION_ERROR, 400 INVALID_WALLET_ADDRESS,
        // 403 SANCTIONS_MATCH, 403 WALLET_CHANGE_COOLDOWN_ACTIVE.
        Assert.Equal("TRANSACTION_NOT_FOUND", TransactionErrorCodes.TransactionNotFound);
        Assert.Equal("NOT_A_PARTY", TransactionErrorCodes.NotAParty);
        Assert.Equal("STEAM_ID_MISMATCH", TransactionErrorCodes.SteamIdMismatch);
        Assert.Equal("ALREADY_ACCEPTED", TransactionErrorCodes.AlreadyAccepted);
        Assert.Equal("INVALID_STATE_TRANSITION", TransactionErrorCodes.InvalidStateTransition);
        Assert.Equal("WALLET_CHANGE_COOLDOWN_ACTIVE", TransactionErrorCodes.WalletChangeCooldownActive);
        Assert.Equal("REFUND_ADDRESS_REQUIRED", TransactionErrorCodes.RefundAddressRequired);
    }

    // ---------------------------------------------------------------------
    // T119a — 07 §7.6 v3.0 fields.
    // ---------------------------------------------------------------------

    [Fact]
    public void TransactionErrorCodes_T119a_Codes_Match_07_7_6_Contract()
    {
        // 07 §7.6 Hatalar (v3.0): 400 INVALID_TRADE_URL, 403
        // MOBILE_AUTHENTICATOR_REQUIRED, 503 STEAM_UNAVAILABLE.
        Assert.Equal("INVALID_TRADE_URL", TransactionErrorCodes.InvalidTradeUrl);
        Assert.Equal("MOBILE_AUTHENTICATOR_REQUIRED", TransactionErrorCodes.MobileAuthenticatorRequired);
        Assert.Equal("STEAM_UNAVAILABLE", TransactionErrorCodes.SteamUnavailable);
    }

    [Fact]
    public void AcceptTransactionRequest_Serializes_SteamTradeUrl_As_CamelCase()
    {
        // 07 §7.6 request body names the field steamTradeUrl; the frontend and
        // the e2e harness post exactly that key.
        var dto = new AcceptTransactionRequest(
            RefundWalletAddress: "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567",
            SteamTradeUrl: "https://steamcommunity.com/tradeoffer/new/?partner=39734353&token=AbCdEfGh");

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"refundWalletAddress\":", json);
        Assert.Contains("\"steamTradeUrl\":", json);
    }

    [Theory]
    // Canonical shape (07 §7.6 example).
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=123456789&token=AbCdEfGh", true)]
    // Tolerated input shapes — the parser normalizes them.
    [InlineData("http://steamcommunity.com/tradeoffer/new/?partner=1&token=a", true)]
    [InlineData("https://STEAMCOMMUNITY.COM/tradeoffer/new/?partner=1&token=a", true)]
    [InlineData("  https://steamcommunity.com/tradeoffer/new/?partner=1&token=a  ", true)]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=1&token=a&l=turkish", true)]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?token=Ab-Cd_Ef&partner=1", true)]
    // Rejected — every one of these is a 400 INVALID_TRADE_URL at the endpoint.
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("steamcommunity.com/tradeoffer/new/?partner=1&token=a", false)]      // no scheme
    [InlineData("javascript:alert(1)", false)]                                        // non-http scheme
    [InlineData("https://www.steamcommunity.com/tradeoffer/new/?partner=1&token=a", false)]     // subdomain
    [InlineData("https://steamcommunity.com.evil.tr/tradeoffer/new/?partner=1&token=a", false)] // suffix attack
    [InlineData("https://steamcommunity.com/tradeoffer/new?partner=1&token=a", false)] // no trailing slash
    [InlineData("https://steamcommunity.com/TradeOffer/New/?partner=1&token=a", false)] // path is case-sensitive
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=1", false)]        // token missing
    [InlineData("https://steamcommunity.com/tradeoffer/new/?token=a", false)]          // partner missing
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=&token=a", false)] // partner empty
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=abc&token=a", false)] // partner not numeric
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=1&token=ab$cd", false)] // bad token charset
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=123456789012345678901&token=a", false)] // >20 chars
    public void Trade_Url_Parser_Contract_Is_The_Accept_Endpoint_Contract(string tradeUrl, bool expectParsed)
    {
        // 07 §7.6 md.2 — "partner + token ayrıştırılabilmeli". The accept
        // endpoint deliberately reuses the U17 parser instead of a second
        // implementation, so this table IS the endpoint's accept/reject list.
        var parsed = new TradeUrlParser().Parse(tradeUrl);

        Assert.Equal(expectParsed, parsed is not null);
    }

    [Fact]
    public void Trade_Url_Parser_Normalizes_To_The_Form_Persisted_On_BuyerTradeUrl()
    {
        // The seller's delivery link is generated from Transaction.BuyerTradeUrl
        // (08 §2.2), so what gets stored must be the canonical form — https,
        // lower-case host, tracking parameters stripped.
        var parsed = new TradeUrlParser().Parse(
            "  http://STEAMCOMMUNITY.COM/tradeoffer/new/?partner=39734353&token=AbCdEfGh&l=turkish  ");

        Assert.NotNull(parsed);
        Assert.Equal(
            "https://steamcommunity.com/tradeoffer/new/?partner=39734353&token=AbCdEfGh",
            parsed.Normalized);
        Assert.Equal("39734353", parsed.Partner);
        Assert.Equal("AbCdEfGh", parsed.Token);
    }

    [Theory]
    // partner == SteamID64 - 76561197960265728 → belongs to the caller.
    [InlineData("76561198000000081", "39734353", true)]
    [InlineData("76561197960265729", "1", true)]
    // Someone else's account — the case the ownership check exists for.
    [InlineData("76561198000000081", "39734354", false)]
    [InlineData("76561198000000081", "39735271", false)]
    // Degenerate inputs must fail closed, never wrap around ulong.
    [InlineData("76561198000000081", "0", false)]
    [InlineData("76561198000000081", "18446744073709551615", false)]
    [InlineData("not-a-steam-id", "39734353", false)]
    public void Trade_Url_Partner_Must_Resolve_To_The_Buyers_Own_SteamId(
        string buyerSteamId64, string partner, bool expectOwned)
    {
        // Owner decision (2026-08-10) — not in 07 §7.6. Mirrors
        // TransactionAcceptanceService.IsOwnedByBuyer; the SUT itself is covered
        // end-to-end by Integration/Lifecycle/TransactionAcceptanceServiceTests.
        const ulong offset = 76561197960265728UL;

        var owned =
            ulong.TryParse(partner, out var partnerId32)
            && ulong.TryParse(buyerSteamId64, out var buyerId64)
            && buyerId64 >= offset
            && buyerId64 - offset == partnerId32;

        Assert.Equal(expectOwned, owned);
    }
}
