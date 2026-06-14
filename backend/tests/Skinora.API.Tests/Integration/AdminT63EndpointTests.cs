using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Skinora.Admin.Domain.Entities;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Disputes.Domain.Entities;
using Skinora.Fraud.Domain.Entities;
using Skinora.Notifications.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T63 — Integration coverage for the four new admin read surfaces (AD1
/// dashboard, AD6 transaction list, AD7 transaction detail, AD10 steam
/// accounts) plus the AD16b (per-user transactions) non-empty regression
/// the T63 wire-up replaced. Permission gates (<c>VIEW_TRANSACTIONS</c>,
/// <c>VIEW_STEAM_ACCOUNTS</c>) plus the "any admin" AD1 surface use the
/// shared <c>PermissionAuthorizationHandler</c> graph through the SQLite
/// in-memory fixture; cross-module data (Notifications / Disputes / Fraud)
/// is seeded directly so the AD7 detail composer is exercised end-to-end.
/// </summary>
public class AdminT63EndpointTests : IClassFixture<AdminT63EndpointTests.Factory>
{
    private const string TestSecret = "t63-admin-test-secret-key-minimum-32-chars-padding!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminT63EndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ============================================================
    // AD1 — GET /admin/dashboard
    // ============================================================

    [Fact]
    public async Task Dashboard_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_RegularUser_Returns403()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildClient(user.Id, user.SteamId, AuthRoles.User, []);

        var response = await client.GetAsync("/api/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_AnyAdmin_Returns200_NoSpecificPermissionRequired()
    {
        var admin = await _factory.CreateUserAsync();
        // Admin role with no specific permissions — AD1 spec is "any admin".
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, []);

        var response = await client.GetAsync("/api/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_EmptySystem_ReturnsZeroCountersAndEmptyArrays()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin, []);

        var response = await client.GetAsync("/api/v1/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        var summary = data.GetProperty("summaryCards");
        Assert.Equal(0, summary.GetProperty("activeTransactions").GetInt32());
        Assert.Equal(0, summary.GetProperty("pendingFlags").GetInt32());
        Assert.Equal(0, summary.GetProperty("dailyCompleted").GetInt32());
        Assert.Equal(0, summary.GetProperty("weeklyCompleted").GetInt32());
        Assert.Equal(0, data.GetProperty("steamAccounts").GetArrayLength());
        Assert.Equal(0, data.GetProperty("recentFlags").GetArrayLength());
    }

    [Fact]
    public async Task Dashboard_PopulatedSystem_AggregatesCountersAndSurfacesData()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync(displayName: "Seller");
        var buyer = await _factory.CreateUserAsync(displayName: "Buyer");

        // 2 active (CREATED + ITEM_ESCROWED), 1 completed today (COMPLETED),
        // 1 cancelled (excluded from active + completed counters).
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.ITEM_ESCROWED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED, completedAt: DateTime.UtcNow.AddHours(-2));
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_BUYER);

        await _factory.CreateFraudFlagAsync(seller.Id, ReviewStatus.PENDING);
        await _factory.CreateFraudFlagAsync(seller.Id, ReviewStatus.APPROVED, admin.Id);

        await _factory.CreatePlatformSteamBotAsync(
            "Bot 1", "76561198900000001", PlatformSteamBotStatus.ACTIVE);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin, []);
        var response = await client.GetAsync("/api/v1/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        var summary = data.GetProperty("summaryCards");
        Assert.Equal(2, summary.GetProperty("activeTransactions").GetInt32());
        Assert.Equal(1, summary.GetProperty("pendingFlags").GetInt32());
        Assert.Equal(1, summary.GetProperty("dailyCompleted").GetInt32());
        Assert.Equal(1, summary.GetProperty("weeklyCompleted").GetInt32());

        Assert.Equal(1, data.GetProperty("steamAccounts").GetArrayLength());
        Assert.Equal(2, data.GetProperty("recentFlags").GetArrayLength());
    }

    [Fact]
    public async Task Dashboard_MoreThanFiveFlags_RecentFlagsCappedAtFiveNewestFirst()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();

        // Seven flags spread by minute so order is deterministic.
        for (var i = 0; i < 7; i++)
        {
            await _factory.CreateFraudFlagAsync(
                seller.Id, ReviewStatus.PENDING,
                createdAtOverride: DateTime.UtcNow.AddMinutes(-i));
        }

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.SuperAdmin, []);
        var response = await client.GetAsync("/api/v1/admin/dashboard");
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        var recentFlags = data.GetProperty("recentFlags");
        Assert.Equal(5, recentFlags.GetArrayLength());

        var first = recentFlags[0].GetProperty("createdAt").GetDateTime();
        var last = recentFlags[4].GetProperty("createdAt").GetDateTime();
        Assert.True(first > last, "recentFlags must be ordered newest-first.");
    }

    // ============================================================
    // AD6 — GET /admin/transactions
    // ============================================================

    [Fact]
    public async Task ListTransactions_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/transactions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListTransactions_AdminWithoutPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.GetAsync("/api/v1/admin/transactions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListTransactions_NoFilters_ReturnsPaginatedItemsNewestFirst()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync(displayName: "S1");
        var buyer = await _factory.CreateUserAsync(displayName: "B1");

        var older = await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED, createdAtOverride: DateTime.UtcNow.AddDays(-2));
        var newer = await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED, createdAtOverride: DateTime.UtcNow.AddHours(-1));

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync("/api/v1/admin/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());

        var items = data.GetProperty("items");
        Assert.Equal(newer.Id.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(older.Id.ToString(), items[1].GetProperty("id").GetString());

        Assert.Equal("S1", items[0].GetProperty("seller").GetProperty("displayName").GetString());
        Assert.Equal("B1", items[0].GetProperty("buyer").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ListTransactions_StatusFilter_NarrowsResults()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync("/api/v1/admin/transactions?status=COMPLETED");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("COMPLETED",
            data.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListTransactions_StatusGroupActive_ExcludesTerminalIncludesFlagged()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.ITEM_ESCROWED);
        // FLAGGED is non-terminal → counted as ACTIVE (mirrors AD1 dashboard).
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.FLAGGED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_BUYER);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync(
            "/api/v1/admin/transactions?statusGroup=ACTIVE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        // CREATED + ITEM_ESCROWED + FLAGGED; COMPLETED + CANCELLED_BUYER excluded.
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListTransactions_StatusGroupCancelled_ReturnsAllCancelledVariants()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_TIMEOUT);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_SELLER);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_BUYER);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CANCELLED_ADMIN);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.FLAGGED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync(
            "/api/v1/admin/transactions?statusGroup=CANCELLED");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(4, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task ListTransactions_StatusGroupFlagged_ReturnsOnlyFlagged()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.FLAGGED);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync(
            "/api/v1/admin/transactions?statusGroup=FLAGGED");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("FLAGGED",
            data.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListTransactions_SearchByItemName_FindsMatch()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED, itemName: "AK-47 | Redline");
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED, itemName: "AWP | Dragon Lore");

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync(
            "/api/v1/admin/transactions?search=Dragon");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("AWP | Dragon Lore",
            data.GetProperty("items")[0].GetProperty("itemName").GetString());
    }

    [Fact]
    public async Task ListTransactions_AmountAndDateRange_FilterCorrectly()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED, price: 50m);
        await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.CREATED, price: 200m);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync(
            "/api/v1/admin/transactions?minAmount=100");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(200m, data.GetProperty("items")[0].GetProperty("price").GetDecimal());
    }

    // ============================================================
    // AD7 — GET /admin/transactions/{id}
    // ============================================================

    [Fact]
    public async Task GetTransactionDetail_UnknownId_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);

        var response = await client.GetAsync(
            $"/api/v1/admin/transactions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetTransactionDetail_FullySeeded_ReturnsAllSections()
    {
        var admin = await _factory.CreateUserAsync(displayName: "Admin");
        var seller = await _factory.CreateUserAsync(displayName: "Seller");
        var buyer = await _factory.CreateUserAsync(displayName: "Buyer");

        var tx = await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.PAYMENT_RECEIVED);

        await _factory.CreateTransactionHistoryAsync(tx.Id, admin.Id,
            from: TransactionStatus.CREATED, to: TransactionStatus.ACCEPTED,
            trigger: "BuyerAccepted");
        await _factory.CreateTransactionHistoryAsync(tx.Id, admin.Id,
            from: TransactionStatus.ACCEPTED, to: TransactionStatus.PAYMENT_RECEIVED,
            trigger: "PaymentDetected");

        var paymentAddressId = await _factory.CreatePaymentAddressAsync(tx.Id,
            address: "TXTronAddressEsc01");
        await _factory.CreateBlockchainTransactionAsync(tx.Id,
            BlockchainTransactionType.BUYER_PAYMENT,
            txHash: "0xpayment123", amount: 102m,
            paymentAddressId: paymentAddressId);

        await _factory.CreateNotificationAsync(seller.Id, tx.Id,
            NotificationType.PAYMENT_RECEIVED);
        await _factory.CreateFraudFlagAsync(seller.Id, ReviewStatus.APPROVED,
            reviewerAdminId: admin.Id, transactionId: tx.Id);
        await _factory.CreateDisputeAsync(tx.Id, buyer.Id,
            DisputeType.DELIVERY, DisputeStatus.OPEN);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync($"/api/v1/admin/transactions/{tx.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");

        Assert.Equal(tx.Id.ToString(), data.GetProperty("id").GetString());
        Assert.Equal("PAYMENT_RECEIVED", data.GetProperty("status").GetString());
        Assert.Equal("Seller", data.GetProperty("seller").GetProperty("displayName").GetString());
        Assert.Equal("Buyer", data.GetProperty("buyer").GetProperty("displayName").GetString());

        Assert.Equal(2, data.GetProperty("statusHistory").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("paymentDetail").ValueKind);
        Assert.Equal("0xpayment123",
            data.GetProperty("paymentDetail").GetProperty("receivedTxHash").GetString());
        Assert.Equal(1, data.GetProperty("notificationHistory").GetArrayLength());
        Assert.Equal(1, data.GetProperty("disputeHistory").GetArrayLength());
        Assert.Equal(1, data.GetProperty("flagHistory").GetArrayLength());

        // PAYMENT_RECEIVED is in the admin-cancellable set per 07 §9.20.
        var actions = data.GetProperty("adminActions");
        Assert.True(actions.GetProperty("canCancel").GetBoolean());
        // Flag was APPROVED, not PENDING, so approve/reject should be false.
        Assert.False(actions.GetProperty("canApproveFlag").GetBoolean());
        Assert.False(actions.GetProperty("canRejectFlag").GetBoolean());
    }

    [Fact]
    public async Task GetTransactionDetail_PendingFlag_AdminActionsAllowApproveAndReject()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();

        var tx = await _factory.CreateTransactionAsync(seller.Id, buyerId: null,
            TransactionStatus.FLAGGED);
        await _factory.CreateFraudFlagAsync(seller.Id, ReviewStatus.PENDING,
            transactionId: tx.Id);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);
        var response = await client.GetAsync($"/api/v1/admin/transactions/{tx.Id}");

        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        var actions = data.GetProperty("adminActions");
        Assert.True(actions.GetProperty("canApproveFlag").GetBoolean());
        Assert.True(actions.GetProperty("canRejectFlag").GetBoolean());
        Assert.True(actions.GetProperty("canCancel").GetBoolean());
    }

    [Fact]
    public async Task GetTransactionDetail_TerminalStateOrOnHold_CannotCancel()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        var completed = await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.COMPLETED);
        var heldId = (await _factory.CreateTransactionAsync(seller.Id, buyer.Id,
            TransactionStatus.PAYMENT_RECEIVED, isOnHold: true)).Id;

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_TRANSACTIONS"]);

        var completedRes = await client.GetAsync(
            $"/api/v1/admin/transactions/{completed.Id}");
        var completedData = (await completedRes.Content
            .ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(completedData.GetProperty("adminActions")
            .GetProperty("canCancel").GetBoolean());

        var heldRes = await client.GetAsync(
            $"/api/v1/admin/transactions/{heldId}");
        var heldData = (await heldRes.Content
            .ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(heldData.GetProperty("adminActions")
            .GetProperty("canCancel").GetBoolean());
    }

    // ============================================================
    // AD10 — GET /admin/steam-accounts
    // ============================================================

    [Fact]
    public async Task SteamAccounts_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/steam-accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SteamAccounts_AdminWithoutPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.GetAsync("/api/v1/admin/steam-accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SteamAccounts_OnlyActiveBots_WarningNull()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.CreatePlatformSteamBotAsync("Bot 1",
            "76561198900000010", PlatformSteamBotStatus.ACTIVE);
        await _factory.CreatePlatformSteamBotAsync("Bot 2",
            "76561198900000011", PlatformSteamBotStatus.ACTIVE);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_STEAM_ACCOUNTS"]);
        var response = await client.GetAsync("/api/v1/admin/steam-accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(2, data.GetProperty("accounts").GetArrayLength());
        Assert.Equal(JsonValueKind.Null,
            data.GetProperty("warningMessage").ValueKind);

        var first = data.GetProperty("accounts")[0];
        Assert.Equal(200, first.GetProperty("dailyTradeOfferLimit").GetInt32());
        Assert.Equal("NONE", first.GetProperty("failoverStatus").GetString());
        Assert.Equal(0, first.GetProperty("recoveryTransactionCount").GetInt32());
    }

    [Fact]
    public async Task SteamAccounts_NonActiveBot_WarningMessageNonNull()
    {
        var admin = await _factory.CreateUserAsync();
        await _factory.CreatePlatformSteamBotAsync("Bot OK",
            "76561198900000020", PlatformSteamBotStatus.ACTIVE);
        await _factory.CreatePlatformSteamBotAsync("Bot Restricted",
            "76561198900000021", PlatformSteamBotStatus.RESTRICTED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_STEAM_ACCOUNTS"]);
        var response = await client.GetAsync("/api/v1/admin/steam-accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        var warning = data.GetProperty("warningMessage").GetString();
        Assert.NotNull(warning);
        Assert.Contains("RESTRICTED", warning);
    }

    // ============================================================
    // AD16b — GET /admin/users/{steamId}/transactions (regression — non-empty)
    // ============================================================

    [Fact]
    public async Task UserTransactions_UserHasTransactions_ReturnsThemAsListItems()
    {
        var admin = await _factory.CreateUserAsync();
        var target = await _factory.CreateUserAsync(displayName: "Target");
        var counterparty = await _factory.CreateUserAsync(displayName: "Counter");

        // target as seller
        await _factory.CreateTransactionAsync(
            target.Id, counterparty.Id, TransactionStatus.CREATED);
        // target as buyer
        await _factory.CreateTransactionAsync(
            counterparty.Id, target.Id, TransactionStatus.COMPLETED);
        // unrelated tx — must not surface
        var thirdParty = await _factory.CreateUserAsync();
        await _factory.CreateTransactionAsync(
            thirdParty.Id, counterparty.Id, TransactionStatus.CREATED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin,
            ["VIEW_USERS"]);
        var response = await client.GetAsync(
            $"/api/v1/admin/users/{target.SteamId}/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
    }

    // ============================================================
    // AD25 — GET /admin/steam-accounts/{botId}/recovery-queue (T103b-2)
    // ============================================================

    [Fact]
    public async Task RecoveryQueue_AdminWithoutViewPermission_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var bot = await _factory.AddBotAsync("Bot R1", "76561198900000030", PlatformSteamBotStatus.RESTRICTED);
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.GetAsync($"/api/v1/admin/steam-accounts/{bot.Id}/recovery-queue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecoveryQueue_UnknownBot_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_STEAM_ACCOUNTS"]);

        var response = await client.GetAsync($"/api/v1/admin/steam-accounts/{Guid.NewGuid()}/recovery-queue");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RecoveryQueue_ReturnsMaterialisedRows()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync(displayName: "Seller");
        var buyer = await _factory.CreateUserAsync(displayName: "Buyer");
        var bot = await _factory.AddBotAsync(
            "Bot R2", "76561198900000031", PlatformSteamBotStatus.RESTRICTED, "restricted");
        var tx = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.ITEM_ESCROWED);
        await _factory.AddBotRecoveryItemAsync(
            bot.Id, tx.Id, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_STEAM_ACCOUNTS"]);
        var response = await client.GetAsync($"/api/v1/admin/steam-accounts/{bot.Id}/recovery-queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(bot.Id, data.GetProperty("botId").GetGuid());
        var items = data.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var row = items[0];
        Assert.Equal(tx.Id, row.GetProperty("transactionId").GetGuid());
        Assert.Equal("PENDING", row.GetProperty("recoveryStatus").GetString());
        Assert.Equal(seller.SteamId, row.GetProperty("sellerSteamId").GetString());
    }

    // ============================================================
    // AD26 — PATCH /admin/steam-accounts/recovery/{id} (T103b-2)
    // ============================================================

    [Fact]
    public async Task UpdateRecovery_WithViewButNotManage_Returns403()
    {
        // The MANAGE_STEAM_RECOVERY gate is separate from the read-only
        // VIEW_STEAM_ACCOUNTS — a viewer cannot mutate recovery state.
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var bot = await _factory.AddBotAsync("Bot R3", "76561198900000032", PlatformSteamBotStatus.RESTRICTED);
        var tx = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await _factory.AddBotRecoveryItemAsync(
            bot.Id, tx.Id, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_STEAM_ACCOUNTS"]);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/steam-accounts/recovery/{recoveryId}",
            new { recoveryStatus = "IN_REVIEW" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRecovery_WithManagePermission_UpdatesAndReturns200()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var bot = await _factory.AddBotAsync("Bot R4", "76561198900000033", PlatformSteamBotStatus.RESTRICTED);
        var tx = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.ITEM_ESCROWED);
        var recoveryId = await _factory.AddBotRecoveryItemAsync(
            bot.Id, tx.Id, BotRecoveryStatus.PENDING, TransactionStatus.ITEM_ESCROWED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_STEAM_RECOVERY"]);
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/steam-accounts/recovery/{recoveryId}",
            new { recoveryStatus = "IN_REVIEW", adminNote = "Investigating with Steam support." }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal("IN_REVIEW", data.GetProperty("recoveryStatus").GetString());
        Assert.Equal("Investigating with Steam support.", data.GetProperty("adminNote").GetString());
    }

    [Fact]
    public async Task UpdateRecovery_UnknownItem_Returns404()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_STEAM_RECOVERY"]);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/steam-accounts/recovery/{Guid.NewGuid()}",
            new { recoveryStatus = "IN_REVIEW" }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private HttpClient BuildClient(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var token = IssueAccessToken(userId, steamId, role, permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(AuthClaimTypes.UserId, userId.ToString()),
            new(AuthClaimTypes.SteamId, steamId),
            new(AuthClaimTypes.Role, role),
        };
        foreach (var permission in permissions)
            claims.Add(new Claim(AuthClaimTypes.Permission, permission));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = creds,
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private sealed class NoopBackgroundJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(
            string jobId, Expression<Action<T>> methodCall, string cronExpression)
        { }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private int _botSuffix;
        private const string SteamIdPrefix = "76561198777640";

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(string? displayName = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = displayName ?? $"T63User{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Transaction> CreateTransactionAsync(
            Guid sellerId,
            Guid? buyerId,
            TransactionStatus status,
            string itemName = "AK-47 | Redline",
            decimal price = 100m,
            DateTime? createdAtOverride = null,
            DateTime? completedAt = null,
            bool isOnHold = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // CK_Transactions_BuyerMethod_SteamId requires TargetBuyerSteamId
            // when BuyerIdentificationMethod = STEAM_ID, so resolve the buyer's
            // SteamId from the seeded user row.
            string? buyerSteamId = null;
            if (buyerId.HasValue)
            {
                buyerSteamId = await db.Set<User>()
                    .AsNoTracking()
                    .Where(u => u.Id == buyerId.Value)
                    .Select(u => u.SteamId)
                    .FirstAsync();
            }

            var nowUtc = DateTime.UtcNow;
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerId = buyerId,
                TargetBuyerSteamId = buyerSteamId,
                BuyerIdentificationMethod = buyerId.HasValue
                    ? BuyerIdentificationMethod.STEAM_ID
                    : BuyerIdentificationMethod.OPEN_LINK,
                BuyerRefundAddress = buyerId.HasValue ? "TXBuyerRefund000000" : null,
                InviteToken = buyerId.HasValue ? null : Guid.NewGuid().ToString("N")[..8],
                ItemAssetId = "100200300",
                ItemClassId = "abc-class",
                ItemName = itemName,
                ItemIconUrl = "https://steamcdn.example/img/test.png",
                StablecoinType = StablecoinType.USDT,
                Price = price,
                CommissionRate = 0.02m,
                CommissionAmount = price * 0.02m,
                TotalAmount = price * 1.02m,
                SellerPayoutAddress = "TXSellerPayout00000",
                PaymentTimeoutMinutes = 1440,
                IsOnHold = isOnHold,
                EmergencyHoldAt = isOnHold ? nowUtc : null,
                EmergencyHoldReason = isOnHold ? "Test hold" : null,
                EmergencyHoldByAdminId = isOnHold ? sellerId : null,
                TimeoutFrozenAt = isOnHold ? nowUtc : null,
                TimeoutFreezeReason = isOnHold ? TimeoutFreezeReason.EMERGENCY_HOLD : null,
                TimeoutRemainingSeconds = isOnHold ? 3600 : null,
                CompletedAt = status == TransactionStatus.COMPLETED
                    ? completedAt ?? nowUtc.AddMinutes(-10)
                    : null,
                CancelledAt = status >= TransactionStatus.CANCELLED_TIMEOUT
                              && status != TransactionStatus.FLAGGED
                    ? nowUtc.AddMinutes(-5)
                    : null,
                CancelledBy = status switch
                {
                    TransactionStatus.CANCELLED_BUYER => CancelledByType.BUYER,
                    TransactionStatus.CANCELLED_SELLER => CancelledByType.SELLER,
                    TransactionStatus.CANCELLED_TIMEOUT => CancelledByType.TIMEOUT,
                    TransactionStatus.CANCELLED_ADMIN => CancelledByType.ADMIN,
                    _ => null,
                },
                CancelReason = status >= TransactionStatus.CANCELLED_TIMEOUT
                               && status != TransactionStatus.FLAGGED
                    ? "Fixture cancel reason"
                    : null,
            };

            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();

            if (createdAtOverride.HasValue)
            {
                // CreatedAt is set by the audit pipeline on Add; re-stamp it
                // here in a second SaveChanges so the desired ordering value
                // survives. AppDbContext.UpdateAuditFields only refreshes
                // CreatedAt on Added entities — Modified rows keep theirs.
                tx.CreatedAt = createdAtOverride.Value;
                await db.SaveChangesAsync();
            }
            return tx;
        }

        public async Task CreateTransactionHistoryAsync(
            Guid transactionId,
            Guid actorId,
            TransactionStatus? from,
            TransactionStatus to,
            string trigger)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<TransactionHistory>().Add(new TransactionHistory
            {
                TransactionId = transactionId,
                PreviousStatus = from,
                NewStatus = to,
                Trigger = trigger,
                ActorType = ActorType.SYSTEM,
                ActorId = actorId,
            });
            await db.SaveChangesAsync();
        }

        public async Task CreateBlockchainTransactionAsync(
            Guid transactionId,
            BlockchainTransactionType type,
            string txHash,
            decimal amount,
            Guid? paymentAddressId = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                // CK_BlockchainTransactions_Type_BuyerPayment requires
                // PaymentAddressId IS NOT NULL when Type=BUYER_PAYMENT.
                PaymentAddressId = type == BlockchainTransactionType.BUYER_PAYMENT
                    ? paymentAddressId
                    : null,
                Type = type,
                TxHash = txHash,
                FromAddress = "TXFrom00000000000000",
                ToAddress = "TXTo000000000000000",
                Amount = amount,
                Token = StablecoinType.USDT,
                Status = BlockchainTransactionStatus.CONFIRMED,
                ConfirmationCount = 21,
                ConfirmedAt = DateTime.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> CreatePaymentAddressAsync(Guid transactionId, string address)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var paymentAddress = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                Address = address,
                HdWalletIndex = 1,
                ExpectedAmount = 102m,
                ExpectedToken = StablecoinType.USDT,
            };
            db.Set<PaymentAddress>().Add(paymentAddress);
            await db.SaveChangesAsync();
            return paymentAddress.Id;
        }

        public async Task CreateNotificationAsync(
            Guid userId, Guid? transactionId, NotificationType type)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<Notification>().Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TransactionId = transactionId,
                Type = type,
                Title = "Test",
                Body = "Test body",
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> CreateFraudFlagAsync(
            Guid userId,
            ReviewStatus status,
            Guid? reviewerAdminId = null,
            Guid? transactionId = null,
            DateTime? createdAtOverride = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var flag = new FraudFlag
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TransactionId = transactionId,
                Scope = transactionId.HasValue
                    ? FraudFlagScope.TRANSACTION_PRE_CREATE
                    : FraudFlagScope.ACCOUNT_LEVEL,
                Type = FraudFlagType.PRICE_DEVIATION,
                Status = status,
                Details = "{\"matchType\":\"price\"}",
                ReviewedAt = status != ReviewStatus.PENDING ? DateTime.UtcNow : null,
                ReviewedByAdminId = status != ReviewStatus.PENDING ? reviewerAdminId : null,
            };
            db.Set<FraudFlag>().Add(flag);
            await db.SaveChangesAsync();

            if (createdAtOverride.HasValue)
            {
                flag.CreatedAt = createdAtOverride.Value;
                await db.SaveChangesAsync();
            }
            return flag.Id;
        }

        public async Task CreateDisputeAsync(
            Guid transactionId,
            Guid openedByUserId,
            DisputeType type,
            DisputeStatus status)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<Dispute>().Add(new Dispute
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                OpenedByUserId = openedByUserId,
                Type = type,
                Status = status,
            });
            await db.SaveChangesAsync();
        }

        public async Task CreatePlatformSteamBotAsync(
            string displayName, string steamId, PlatformSteamBotStatus status)
            => await AddBotAsync(displayName, steamId, status);

        public async Task<PlatformSteamBot> AddBotAsync(
            string displayName, string steamId, PlatformSteamBotStatus status, string? restrictionReason = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Interlocked.Increment(ref _botSuffix);
            var bot = new PlatformSteamBot
            {
                Id = Guid.NewGuid(),
                DisplayName = displayName,
                SteamId = steamId,
                Status = status,
                RestrictionReason = restrictionReason,
                ActiveEscrowCount = 0,
                DailyTradeOfferCount = 0,
                LastHealthCheckAt = DateTime.UtcNow,
            };
            db.Set<PlatformSteamBot>().Add(bot);
            await db.SaveChangesAsync();
            return bot;
        }

        public async Task<Guid> AddBotRecoveryItemAsync(
            Guid botId, Guid transactionId, BotRecoveryStatus status, TransactionStatus statusAtRestriction)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new BotRecoveryItem
            {
                Id = Guid.NewGuid(),
                PlatformSteamBotId = botId,
                TransactionId = transactionId,
                RecoveryStatus = status,
                StatusAtRestriction = statusAtRestriction,
            };
            db.Set<BotRecoveryItem>().Add(item);
            await db.SaveChangesAsync();
            return item.Id;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<Notification>().RemoveRange(
                db.Set<Notification>().IgnoreQueryFilters().ToList());
            db.Set<NotificationDelivery>().RemoveRange(
                db.Set<NotificationDelivery>().IgnoreQueryFilters().ToList());
            db.Set<Dispute>().RemoveRange(
                db.Set<Dispute>().IgnoreQueryFilters().ToList());
            db.Set<FraudFlag>().RemoveRange(
                db.Set<FraudFlag>().IgnoreQueryFilters().ToList());
            // T103b-2 — clear recovery items before their Transaction / bot / user FKs.
            db.Set<BotRecoveryItem>().RemoveRange(
                db.Set<BotRecoveryItem>().IgnoreQueryFilters().ToList());
            db.Set<BlockchainTransaction>().RemoveRange(
                db.Set<BlockchainTransaction>().ToList());
            db.Set<PaymentAddress>().RemoveRange(
                db.Set<PaymentAddress>().IgnoreQueryFilters().ToList());
            db.Set<Transaction>().RemoveRange(
                db.Set<Transaction>().IgnoreQueryFilters().ToList());
            db.Set<PlatformSteamBot>().RemoveRange(
                db.Set<PlatformSteamBot>().IgnoreQueryFilters().ToList());

            // TransactionHistory + AuditLog are IAppendOnly (06 §4.2);
            // EnforceAppendOnly rejects EntityState.Deleted. Raw SQL keeps
            // the production guard intact while clearing test fixtures.
            db.Database.ExecuteSqlRaw("DELETE FROM TransactionHistory");
            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");

            db.Set<AdminUserRole>().RemoveRange(
                db.Set<AdminUserRole>().IgnoreQueryFilters().ToList());
            db.Set<AdminRolePermission>().RemoveRange(
                db.Set<AdminRolePermission>().IgnoreQueryFilters().ToList());
            db.Set<AdminRole>().RemoveRange(
                db.Set<AdminRole>().IgnoreQueryFilters().ToList());

            var seedIds = new[] { SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(
                db.Set<User>().IgnoreQueryFilters().Where(u => !seedIds.Contains(u.Id)));
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
            builder.UseSetting("Jwt:PreviousSecret", "");

            builder.UseSetting("SteamOpenId:Realm", "https://skinora.test");
            builder.UseSetting("SteamOpenId:ReturnToUrl",
                "https://skinora.test/api/v1/auth/steam/callback");
            builder.UseSetting("SteamOpenId:FrontendCallbackUrl",
                "https://localhost:3000/auth/callback");
            builder.UseSetting("SteamOpenId:DefaultReturnPath", "/dashboard");
            builder.UseSetting("SteamOpenId:WebApiKey", "");

            builder.ConfigureServices(services =>
            {
                var efDescriptors = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                        d.ServiceType == typeof(DbContextOptions) ||
                        d.ServiceType == typeof(AppDbContext) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")) ||
                        (d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore",
                            StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in efDescriptors) services.Remove(d);

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                var hangfireDescriptors = services
                    .Where(d =>
                        (d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationType?.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name?
                            .StartsWith("Hangfire", StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in hangfireDescriptors) services.Remove(d);

                var startupHookDescriptors = services
                    .Where(d =>
                        d.ImplementationType == typeof(OutboxStartupHook) ||
                        d.ImplementationType == typeof(SettingsBootstrapHook))
                    .ToList();
                foreach (var d in startupHookDescriptors) services.Remove(d);

                services.RemoveAll<IBackgroundJobScheduler>();
                services.AddSingleton<IBackgroundJobScheduler, NoopBackgroundJobScheduler>();

                services.RemoveAll<IDistributedLockProvider>();
                services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

                var healthCheckDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("HealthCheck",
                        StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var d in healthCheckDescriptors) services.Remove(d);
                services.AddHealthChecks();

                services.RemoveAll<IRateLimiterStore>();
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }
}
