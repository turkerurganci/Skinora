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
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Transactions.Application.Steam;
using Skinora.Users.Application.Settings;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// HTTP-level smoke coverage for the T83a list endpoint (07 §7.1) and the
/// T45/T46/T51/T60 lifecycle endpoints (07 §7.2–§7.7, §7.11): wiring, auth
/// gate, rate-limit policy, response envelope. Deeper service logic is
/// verified by <c>Skinora.Transactions.Tests/{Unit,Integration}/Lifecycle/*</c>.
/// </summary>
public class TransactionLifecycleEndpointTests : IClassFixture<TransactionLifecycleEndpointTests.Factory>
{
    private const string TestSecret = "tx-lifecycle-test-secret-key-32chars!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198777999001";
    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    /// <summary>T119a — SteamID64 → SteamID32 offset (07 §7.6 ownership check).</summary>
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    /// <summary>
    /// T119a — a well-formed trade URL whose <c>partner</c> resolves to
    /// <paramref name="steamId64"/>. The accept endpoint rejects any URL that
    /// belongs to somebody else, so every accept test needs the caller's own.
    /// </summary>
    private static string TradeUrlFor(string steamId64)
        => $"https://steamcommunity.com/tradeoffer/new/?partner={ulong.Parse(steamId64) - SteamId64ToId32Offset}&token=AbCdEfGh";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public TransactionLifecycleEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Eligibility_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/transactions/eligibility");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- T83a: GET /transactions (07 §7.1) ----------

    [Fact]
    public async Task List_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Authenticated_Returns_PagedResult_Envelope()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/transactions?tab=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out _));
        Assert.True(data.TryGetProperty("totalCount", out _));
        Assert.Equal(1, data.GetProperty("page").GetInt32());
        Assert.Equal(20, data.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task List_Default_Tab_Is_Active_When_Query_Param_Omitted()
    {
        var seller = await _factory.CreateUserAsync();
        await _factory.SeedTransactionAsync(seller.Id,
            status: Skinora.Shared.Enums.TransactionStatus.CREATED);
        await _factory.SeedTransactionAsync(seller.Id,
            status: Skinora.Shared.Enums.TransactionStatus.COMPLETED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.GetAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        var item = data.GetProperty("items")[0];
        Assert.Equal("CREATED", item.GetProperty("status").GetString());
        Assert.Equal("seller", item.GetProperty("userRole").GetString());
    }

    [Fact]
    public async Task List_Cancelled_Tab_Returns_Only_Cancelled_Rows()
    {
        var seller = await _factory.CreateUserAsync();
        await _factory.SeedTransactionAsync(seller.Id,
            status: Skinora.Shared.Enums.TransactionStatus.CANCELLED_SELLER);
        await _factory.SeedTransactionAsync(seller.Id,
            status: Skinora.Shared.Enums.TransactionStatus.CREATED);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.GetAsync("/api/v1/transactions?tab=cancelled");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("CANCELLED_SELLER",
            data.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task List_Excludes_Other_Users_Transactions()
    {
        var caller = await _factory.CreateUserAsync();
        var someoneElse = await _factory.CreateUserAsync();
        await _factory.SeedTransactionAsync(someoneElse.Id,
            status: Skinora.Shared.Enums.TransactionStatus.CREATED);

        var client = BuildAuthenticatedClient(caller.Id, caller.SteamId);
        var response = await client.GetAsync("/api/v1/transactions?tab=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(0, body.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Eligibility_Authenticated_ReturnsDto()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/transactions/eligibility");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("eligible").GetBoolean());
        Assert.True(data.GetProperty("mobileAuthenticatorActive").GetBoolean());
    }

    [Fact]
    public async Task Params_Authenticated_ReturnsDto()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/transactions/params");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.TryGetProperty("minPrice", out _));
        Assert.True(data.TryGetProperty("maxPrice", out _));
        Assert.True(data.TryGetProperty("commissionRate", out _));
        Assert.True(data.TryGetProperty("paymentTimeout", out _));
        Assert.True(data.TryGetProperty("supportedStablecoins", out _));
    }

    [Fact]
    public async Task Create_Happy_Path_Returns_201_And_Persists_Transaction()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("CREATED", data.GetProperty("status").GetString());

        // Verify the OutboxMessage row was written in the same SaveChanges
        // (T45 acceptance criterion: TransactionCreatedEvent emitted via outbox).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Set<OutboxMessage>().AsNoTracking().ToList());
    }

    [Fact]
    public async Task Create_Below_Minimum_Price_Returns_422()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "1.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        // Force the configured minimum to be larger than the request price.
        await _factory.ConfigureSettingAsync("min_transaction_amount", "10");

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("PRICE_OUT_OF_RANGE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_Anonymous_Returns_Public_Variant()
    {
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        // Public variant contract: userRole absent (suppressed via WhenWritingNull),
        // commission/total absent, availableActions has requiresLogin=true.
        Assert.False(data.TryGetProperty("commissionAmount", out _));
        Assert.False(data.TryGetProperty("totalAmount", out _));
        var actions = data.GetProperty("availableActions");
        Assert.False(actions.GetProperty("canAccept").GetBoolean());
        Assert.True(actions.GetProperty("requiresLogin").GetBoolean());
    }

    [Fact]
    public async Task Detail_Authenticated_Buyer_Returns_Full_Variant()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        // Steam ID match → service resolves the target buyer as
        // role="buyer" even before BuyerId is set (03 §3.2 step 1).
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("buyer", data.GetProperty("userRole").GetString());
        Assert.Equal("102.00", data.GetProperty("totalAmount").GetString());
    }

    [Fact]
    public async Task Detail_Non_Party_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.GetAsync($"/api/v1/transactions/{transactionId:D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_A_PARTY",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_Not_Found_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/transactions/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Accept_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/accept",
            new { refundWalletAddress = ValidWallet, steamTradeUrl = TradeUrlFor(SteamId) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_Happy_Path_Transitions_To_Accepted_And_Emits_Outbox()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = ValidWallet, steamTradeUrl = TradeUrlFor(buyer.SteamId) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("ACCEPTED", data.GetProperty("status").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Set<OutboxMessage>().AsNoTracking().ToList());

        // T119a — the mandatory v3.0 field reaches the row through the HTTP
        // layer, in its normalized form (06 §3.5).
        var persisted = db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
            .AsNoTracking().Single(t => t.Id == transactionId);
        Assert.Equal(TradeUrlFor(buyer.SteamId), persisted.BuyerTradeUrl);
    }

    [Fact]
    public async Task Accept_Steam_Id_Mismatch_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: "76561198000099999");

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = ValidWallet, steamTradeUrl = TradeUrlFor(stranger.SteamId) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("STEAM_ID_MISMATCH",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Accept_Invalid_Wallet_Returns_400()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = "NOT_A_TRC20_ADDRESS", steamTradeUrl = TradeUrlFor(buyer.SteamId) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_WALLET_ADDRESS",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- T119a: accept v3.0 fields (07 §7.6) ----------

    [Fact]
    public async Task Accept_Invalid_Trade_Url_Returns_400()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/accept",
            new { refundWalletAddress = ValidWallet, steamTradeUrl = "steamcommunity.com/tradeoffer/new/?partner=1" });

        // 400 — 07 §7.6 pins this code at 400 even though the U17 profile-save
        // path answers 422 for the same code (documented divergence).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_TRADE_URL",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Accept_Without_Mobile_Authenticator_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        _factory.TradeHold.Active = false;
        try
        {
            var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
            var response = await client.PostAsJsonAsync(
                $"/api/v1/transactions/{transactionId:D}/accept",
                new { refundWalletAddress = ValidWallet, steamTradeUrl = TradeUrlFor(buyer.SteamId) });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("MOBILE_AUTHENTICATOR_REQUIRED",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.TradeHold.Active = true;
        }
    }

    [Fact]
    public async Task Accept_When_Steam_Unreachable_Returns_503()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId);

        _factory.TradeHold.Available = false;
        try
        {
            var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
            var response = await client.PostAsJsonAsync(
                $"/api/v1/transactions/{transactionId:D}/accept",
                new { refundWalletAddress = ValidWallet, steamTradeUrl = TradeUrlFor(buyer.SteamId) });

            // Fail-closed (08 §2.2) and retryable — not a 403 telling the buyer
            // to fix an authenticator that may be perfectly fine.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("STEAM_UNAVAILABLE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.TradeHold.Available = true;
        }
    }

    // ---------- T51: POST /transactions/:id/cancel (07 §7.7) ----------

    [Fact]
    public async Task Cancel_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/cancel",
            new { reason = "Yeterince uzun bir iptal sebebi" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Happy_Path_Returns_200_And_Persists_CancelledSeller()
    {
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/cancel",
            new { reason = "Bu işlemi sonlandırmak istiyorum" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("CANCELLED_SELLER", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("paymentRefunded").GetBoolean());
        // v3.0 — itemReturned was dropped from the response: the platform never
        // holds the item, so a cancellation can only move money (07 §7.7, 02 §9).
        Assert.False(data.TryGetProperty("itemReturned", out _));

        // Outbox row written in the same SaveChanges (TransactionCancelledEvent).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotEmpty(db.Set<OutboxMessage>().AsNoTracking().ToList());
    }

    [Fact]
    public async Task Cancel_Non_Party_Returns_403()
    {
        var seller = await _factory.CreateUserAsync();
        var stranger = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(stranger.Id, stranger.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/cancel",
            new { reason = "Bu işlem benim değil ama deniyorum" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_A_PARTY",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_Reason_Too_Short_Returns_400_Validation()
    {
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/cancel",
            new { reason = "kısa" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("CANCEL_REASON_REQUIRED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_Post_Payment_By_Buyer_Returns_422_PaymentAlreadySent()
    {
        // 07 §7.7 (v3.0) — post-payment cancel is asymmetric: 422 is the BUYER's
        // answer. The seller may still back out (covered below).
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id,
            targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED,
            buyerId: buyer.Id);

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/cancel",
            new { reason = "Ödeme sonrası iptal denemesi" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("PAYMENT_ALREADY_SENT",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancel_Post_Payment_By_Seller_Returns_200_And_Refunds_Buyer()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id,
            targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED,
            buyerId: buyer.Id);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}/cancel",
            new { reason = "Item göndermekten vazgeçtim" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("CANCELLED_SELLER", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("paymentRefunded").GetBoolean());
    }

    [Fact]
    public async Task Cancel_Not_Found_Returns_404()
    {
        var seller = await _factory.CreateUserAsync();

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/cancel",
            new { reason = "Geçersiz transaction id testi" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_Eligibility_Fail_Returns_422()
    {
        // No MA → eligibility fails with MOBILE_AUTHENTICATOR_REQUIRED.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = false;
            u.DefaultPayoutAddress = ValidWallet;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var request = new
        {
            itemAssetId = "27348562891",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = ValidWallet,
        };

        var response = await client.PostAsJsonAsync("/api/v1/transactions", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("MOBILE_AUTHENTICATOR_REQUIRED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- helpers ----------

    private HttpClient BuildAuthenticatedClient(Guid userId, string steamId)
    {
        var token = IssueAccessToken(userId, steamId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(Guid userId, string steamId)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(AuthClaimTypes.UserId, userId.ToString()),
                new Claim(AuthClaimTypes.SteamId, steamId),
                new Claim(AuthClaimTypes.Role, AuthRoles.User),
            }),
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

    /// <summary>
    /// In-process replacement for <see cref="ISteamInventoryReader"/>. Tests
    /// register tradeable items via <see cref="Register"/>; everything else
    /// returns <c>null</c> so the controller correctly maps to
    /// <c>ITEM_NOT_IN_INVENTORY</c>.
    /// </summary>
    private sealed class FakeSteamInventoryReader : ISteamInventoryReader
    {
        private readonly Dictionary<(string steamId, string assetId), InventoryItemSnapshot> _items = [];

        public void Register(string steamId, string assetId, string name)
            => _items[(steamId, assetId)] = new InventoryItemSnapshot(
                AssetId: assetId,
                ClassId: "test-class",
                InstanceId: "test-instance",
                Name: name,
                MarketHashName: name,
                IconUrl: null,
                Exterior: null,
                Type: null,
                InspectLink: null,
                IsTradeable: true);

        public Task<InventoryItemSnapshot?> TryGetItemAsync(
            string steamId64, string itemAssetId, CancellationToken cancellationToken)
            => Task.FromResult(_items.TryGetValue((steamId64, itemAssetId), out var item) ? item : null);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private readonly FakeSteamInventoryReader _inventory = new();
        private int _userSuffix;

        /// <summary>
        /// T119a — buyer Mobile Authenticator probe (07 §7.6 md.3). Defaults to
        /// "Steam reachable, MA active"; individual tests flip it and reset it in
        /// a finally block. Reuses the U17 endpoint tests' stub — one shape for
        /// the whole <see cref="ITradeHoldChecker"/> seam.
        /// </summary>
        public AccountSettingsEndpointTests.ConfigurableTradeHoldStub TradeHold { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public void SeedInventoryItem(string steamId, string assetId, string name)
            => _inventory.Register(steamId, assetId, name);

        public async Task ConfigureSettingAsync(string key, string value)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
                .FirstOrDefault(s => s.Key == key);
            if (existing is null)
            {
                db.Set<Skinora.Platform.Domain.Entities.SystemSetting>().Add(
                    new Skinora.Platform.Domain.Entities.SystemSetting
                    {
                        Id = Guid.NewGuid(),
                        Key = key,
                        Value = value,
                        IsConfigured = true,
                        DataType = "string",
                        Category = "Test",
                    });
            }
            else
            {
                existing.Value = value;
                existing.IsConfigured = true;
            }
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedTransactionAsync(
            Guid sellerId,
            string? targetBuyerSteamId = null,
            Skinora.Shared.Enums.TransactionStatus status = Skinora.Shared.Enums.TransactionStatus.CREATED,
            Guid? buyerId = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nowUtc = DateTime.UtcNow;
            var isCancelled =
                status is Skinora.Shared.Enums.TransactionStatus.CANCELLED_TIMEOUT
                    or Skinora.Shared.Enums.TransactionStatus.CANCELLED_SELLER
                    or Skinora.Shared.Enums.TransactionStatus.CANCELLED_BUYER
                    or Skinora.Shared.Enums.TransactionStatus.CANCELLED_ADMIN;
            var transaction = new Skinora.Transactions.Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerIdentificationMethod = Skinora.Shared.Enums.BuyerIdentificationMethod.STEAM_ID,
                // CK_Transactions_BuyerMethod_SteamId: STEAM_ID method requires
                // TargetBuyerSteamId NOT NULL. Tests that don't care about a
                // specific buyer pass an arbitrary non-matching ID.
                TargetBuyerSteamId = targetBuyerSteamId ?? "76561198999999999",
                BuyerId = buyerId,
                // Required from BuyerAccept onwards (06 §3.5) and read by the
                // refund path when a post-payment cancel unwinds the escrow.
                BuyerRefundAddress = buyerId is null ? null : ValidWallet,
                // T119a — same 06 §3.5 bracket: NOT NULL once a buyer exists.
                BuyerTradeUrl = buyerId is null ? null : TradeUrlFor(SteamId),
                PaymentReceivedAt =
                    status is Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED
                        or Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                        or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                        ? DateTime.UtcNow.AddMinutes(-5)
                        : null,
                ItemAssetId = Guid.NewGuid().ToString("N")[..12],
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                StablecoinType = Skinora.Shared.Enums.StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = ValidWallet,
                PaymentTimeoutMinutes = 1440,
                AcceptDeadline = status == Skinora.Shared.Enums.TransactionStatus.CREATED
                    ? nowUtc.AddHours(1)
                    : null,
                // CK_Transactions_Cancel — CANCELLED_* requires the trio
                // {CancelledBy, CancelReason, CancelledAt} NOT NULL.
                CancelledAt = isCancelled ? nowUtc.AddMinutes(-1) : null,
                CancelledBy = isCancelled ? Skinora.Shared.Enums.CancelledByType.BUYER : null,
                CancelReason = isCancelled ? "Test iptal sebebi (>=10 char)" : null,
                CompletedAt = status == Skinora.Shared.Enums.TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-1)
                    : null,
            };
            db.Set<Skinora.Transactions.Domain.Entities.Transaction>().Add(transaction);
            await db.SaveChangesAsync();
            return transaction.Id;
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamId}{suffix:D2}",
                SteamDisplayName = "Tester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-200),
            };
            customize?.Invoke(user);
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // WP15 — TransactionHistory (IAppendOnly; FK→Transaction/User = NO
            // ACTION, 06 §3.6) must be cleared before its parents. ExecuteDelete
            // bypasses the EnforceAppendOnly guard the ChangeTracker path enforces.
            db.Set<Skinora.Transactions.Domain.Entities.TransactionHistory>().ExecuteDelete();
            db.Set<OutboxMessage>().RemoveRange(db.Set<OutboxMessage>());
            db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
                .RemoveRange(db.Set<Skinora.Transactions.Domain.Entities.Transaction>());
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
            db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
                .RemoveRange(db.Set<Skinora.Platform.Domain.Entities.SystemSetting>());
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

                services.RemoveAll<IRefreshTokenCache>();
                services.AddSingleton<IRefreshTokenCache, NullRefreshTokenCache>();

                // T45 — replace the Steam inventory stub with the in-test fake
                // so the seller's items are predictable from test code.
                services.RemoveAll<ISteamInventoryReader>();
                services.AddSingleton<ISteamInventoryReader>(_inventory);

                // T119a — the accept endpoint now probes the buyer's Mobile
                // Authenticator live (07 §7.6). Without this swap the registered
                // HttpSteamTradeHoldClient would try to reach a sidecar this host
                // never configures, fail closed, and turn every accept into a 503.
                services.RemoveAll<ITradeHoldChecker>();
                services.AddSingleton<ITradeHoldChecker>(TradeHold);
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
