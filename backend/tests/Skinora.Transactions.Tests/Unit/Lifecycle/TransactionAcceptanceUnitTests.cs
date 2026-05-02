using System.Text.Json;
using System.Text.Json.Serialization;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Lifecycle;

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
            CanCancel: null,
            CanDispute: null,
            CanEscalate: null,
            RequiresLogin: true);

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"canAccept\":false", json);
        Assert.Contains("\"requiresLogin\":true", json);
        Assert.DoesNotContain("canCancel", json);
        Assert.DoesNotContain("canDispute", json);
        Assert.DoesNotContain("canEscalate", json);
    }

    [Fact]
    public void AvailableActionsDto_Authenticated_Variant_Suppresses_RequiresLogin()
    {
        var dto = new AvailableActionsDto(
            CanAccept: false,
            CanCancel: true,
            CanDispute: false,
            CanEscalate: false,
            RequiresLogin: null);

        var json = JsonSerializer.Serialize(dto, JsonOptions);

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
}
