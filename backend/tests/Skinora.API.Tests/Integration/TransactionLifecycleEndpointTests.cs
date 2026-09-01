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
using Skinora.Transactions.Domain.Entities;
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

    /// <summary>
    /// The wire-level half of the payout-address fix, and the ONLY test that can
    /// catch its regression. `System.Text.Json` silently drops unknown members,
    /// so an old client that still sends `sellerWalletAddress` gets a 201 either
    /// way — every existing create test would stay green even if the service
    /// went back to reading the body. This one plants a DIFFERENT address in the
    /// body and asserts the persisted column still holds the PROFILE address.
    /// </summary>
    [Fact]
    public async Task Create_Ignores_SellerWalletAddress_In_Request_Body()
    {
        const string attackerAddress = "TAttackerAddressABCDEFGH23456789zy";
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562893", "AWP | Asiimov");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var response = await client.PostAsJsonAsync("/api/v1/transactions", new
        {
            itemAssetId = "27348562893",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
            sellerWalletAddress = attackerAddress,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = body.GetProperty("data").GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = db.Set<Transaction>().AsNoTracking().Single(t => t.Id == id);
        Assert.Equal(ValidWallet, persisted.SellerPayoutAddress);
        Assert.NotEqual(attackerAddress, persisted.SellerPayoutAddress);
    }

    /// <summary>
    /// With the body field gone, a seller with no profile payout address can no
    /// longer reach a listing at all. This reason was always produced by
    /// `TransactionEligibilityService`, but the wizard filtered it out and let
    /// the seller fill four steps before hitting a 422 dead end
    /// (`Prova-InlineSellerWalletUnreachable`); now it is the gate, up front.
    /// </summary>
    [Fact]
    public async Task Create_Returns_422_SELLER_WALLET_ADDRESS_MISSING_When_Profile_Has_No_Address()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = null;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562894", "Glock-18 | Fade");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var response = await client.PostAsJsonAsync("/api/v1/transactions", new
        {
            itemAssetId = "27348562894",
            stablecoin = "USDT",
            price = "100.00",
            paymentTimeoutHours = 24,
            buyerIdentificationMethod = "STEAM_ID",
            buyerSteamId = "76561198000000999",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SELLER_WALLET_ADDRESS_MISSING",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_Returns_422_INVENTORY_PRIVATE_When_Seller_Profile_Hidden()
    {
        // T121 — 07 §6.1's inventory vocabulary, reused on the create path. The
        // item IS seeded: the response must be decided by visibility, not by
        // the asset lookup.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");
        _factory.InventoryVisibilityOverride = InventoryVisibility.Private;

        try
        {
            var client = BuildAuthenticatedClient(user.Id, user.SteamId);
            var response = await client.PostAsJsonAsync("/api/v1/transactions", CreateRequestBody());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("INVENTORY_PRIVATE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.InventoryVisibilityOverride = null;
        }
    }

    [Fact]
    public async Task Create_Returns_503_STEAM_UNAVAILABLE_When_Inventory_Unreadable()
    {
        // T121 — a Steam outage is undecided and retryable, so it must not be
        // reported as 422 ITEM_NOT_IN_INVENTORY (08 §2.3).
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");
        _factory.InventoryVisibilityOverride = InventoryVisibility.Unavailable;

        try
        {
            var client = BuildAuthenticatedClient(user.Id, user.SteamId);
            var response = await client.PostAsJsonAsync("/api/v1/transactions", CreateRequestBody());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("STEAM_UNAVAILABLE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.InventoryVisibilityOverride = null;
        }
    }

    [Fact]
    public async Task Create_Returns_422_ITEM_NOT_IN_INVENTORY_When_Inventory_Readable_But_Asset_Absent()
    {
        // The third leg of the same fork: the inventory WAS read, so "not
        // there" is a finding the seller can act on. Kept next to the two
        // tests above so the three responses stay visibly distinct.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        var response = await client.PostAsJsonAsync("/api/v1/transactions", CreateRequestBody());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ITEM_NOT_IN_INVENTORY",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_Returns_422_ITEM_ALREADY_LISTED_On_Second_Create_For_Same_Asset()
    {
        // T128 — 02 §2.3 over the wire. The first create succeeds and the
        // second one, identical, must come back as a business rejection the
        // seller can read rather than the 500 the bare unique index produced.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.MobileAuthenticatorVerified = true;
            u.DefaultPayoutAddress = ValidWallet;
        });
        _factory.SeedInventoryItem(user.SteamId, "27348562891", "AK-47 | Redline");
        await _factory.ConfigureSettingAsync("max_concurrent_transactions", "5");

        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var first = await client.PostAsJsonAsync("/api/v1/transactions", CreateRequestBody());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/transactions", CreateRequestBody());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ITEM_ALREADY_LISTED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    private static object CreateRequestBody() => new
    {
        itemAssetId = "27348562891",
        stablecoin = "USDT",
        price = "100.00",
        paymentTimeoutHours = 24,
        buyerIdentificationMethod = "STEAM_ID",
        buyerSteamId = "76561198000000999",
    };

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

    // ---------- T123: POST /transactions/:id/confirm-ready (07 §7.6a) ----------

    [Fact]
    public async Task ConfirmReady_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReady_Happy_Path_Returns_200_With_The_Payment_Window()
    {
        var (seller, transactionId, client) = await SetUpConfirmReadyAsync();

        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal("SELLER_CONFIRMED", data.GetProperty("status").GetString());
        Assert.True(data.TryGetProperty("sellerReadyConfirmedAt", out _));
        Assert.True(data.TryGetProperty("paymentDeadline", out _));
        // Always emitted, not only when false: the seller is being told which
        // 02 §9.2 evidence paths this transaction will have.
        Assert.True(data.GetProperty("buyerInventoryVisible").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
            .AsNoTracking().Single(t => t.Id == transactionId);
        Assert.Equal(Skinora.Shared.Enums.TransactionStatus.SELLER_CONFIRMED, persisted.Status);
        Assert.NotNull(persisted.SellerReadyConfirmedAt);
        Assert.NotNull(persisted.PaymentDeadline);
        Assert.NotEmpty(db.Set<OutboxMessage>().AsNoTracking().ToList());
        _ = seller;
    }

    [Fact]
    public async Task ConfirmReady_Buyer_Calling_Returns_403_NotAParty()
    {
        var buyer = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        const string assetId = "27348562891";
        _factory.SeedInventoryItem(seller.SteamId, assetId, "AK-47 | Redline");
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.ACCEPTED,
            buyerId: buyer.Id, itemAssetId: assetId,
            buyerTradeUrl: TradeUrlFor(buyer.SteamId));

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_A_PARTY", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmReady_Item_Gone_Returns_409_ItemNoLongerAvailable()
    {
        // The seller's inventory is readable and the asset is not in it — the
        // one branch that licenses a positive "it is gone" verdict (08 §2.3).
        var (_, transactionId, client) = await SetUpConfirmReadyAsync(registerItem: false);

        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("ITEM_NO_LONGER_AVAILABLE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmReady_Seller_Inventory_Private_Returns_422_InventoryPrivate()
    {
        // 422, not the 409 above: a hidden profile is absence of information.
        // Same code/status the create path already uses for the same read
        // (07 §7.2, T121), so the seller sees one dictionary across the flow.
        var (_, transactionId, client) = await SetUpConfirmReadyAsync();
        _factory.InventoryVisibilityOverride = InventoryVisibility.Private;
        try
        {
            var response = await client.PostAsync(
                $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("INVENTORY_PRIVATE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.InventoryVisibilityOverride = null;
        }
    }

    [Fact]
    public async Task ConfirmReady_Steam_Unreachable_Returns_503()
    {
        var (_, transactionId, client) = await SetUpConfirmReadyAsync();
        _factory.InventoryVisibilityOverride = InventoryVisibility.Unavailable;
        try
        {
            var response = await client.PostAsync(
                $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("STEAM_UNAVAILABLE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.InventoryVisibilityOverride = null;
        }
    }

    [Fact]
    public async Task ConfirmReady_Buyer_MA_Inactive_Returns_403_With_The_Buyer_Specific_Code()
    {
        var (_, transactionId, client) = await SetUpConfirmReadyAsync();
        _factory.TradeHold.Active = false;
        try
        {
            var response = await client.PostAsync(
                $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            Assert.Equal("BUYER_MOBILE_AUTHENTICATOR_INACTIVE",
                body.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _factory.TradeHold.Active = true;
        }
    }

    [Fact]
    public async Task ConfirmReady_Unreadable_Buyer_Inventory_Still_Returns_200()
    {
        // 03 §2.3 md.3 — the transaction advances; only the inventory-evidence
        // path closes. Blocking would punish both parties for the buyer's
        // privacy setting.
        var (_, transactionId, client) = await SetUpConfirmReadyAsync();
        _factory.BaselineVisibilityOverride = InventoryVisibility.Private;
        try
        {
            var response = await client.PostAsync(
                $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
                .GetProperty("data");
            Assert.False(data.GetProperty("buyerInventoryVisible").GetBoolean());
        }
        finally
        {
            _factory.BaselineVisibilityOverride = null;
        }
    }

    [Fact]
    public async Task ConfirmReady_Wrong_State_Returns_409()
    {
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(seller.Id);

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_STATE_TRANSITION",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmReady_Unknown_Transaction_Returns_404()
    {
        var seller = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);

        var response = await client.PostAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/confirm-ready", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Payment_Address_Is_Only_Disclosed_After_ConfirmReady()
    {
        // AC4 end-to-end over HTTP: the deposit address exists from creation
        // (allocator runs there) but 03 §2.3 says the buyer must not see it
        // until the seller re-confirms the item is still sendable.
        var (_, transactionId, sellerClient) = await SetUpConfirmReadyAsync();
        var buyerId = await BuyerIdOfAsync(transactionId);
        var buyer = await UserByIdAsync(buyerId);
        await SeedPaymentAddressAsync(transactionId);

        var buyerClient = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var before = (await (await buyerClient.GetAsync($"/api/v1/transactions/{transactionId:D}"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(before.TryGetProperty("payment", out _));

        await sellerClient.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-ready", content: null);

        var after = (await (await buyerClient.GetAsync($"/api/v1/transactions/{transactionId:D}"))
            .Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        var payment = after.GetProperty("payment");
        Assert.Equal("TPaymentAddr1234567890abcdef1234", payment.GetProperty("address").GetString());
        Assert.Equal("Tron (TRC-20)", payment.GetProperty("network").GetString());
    }

    private async Task<(User Seller, Guid TransactionId, HttpClient Client)> SetUpConfirmReadyAsync(
        bool registerItem = true)
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        const string assetId = "27348562891";
        if (registerItem)
            _factory.SeedInventoryItem(seller.SteamId, assetId, "AK-47 | Redline");

        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id,
            targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.ACCEPTED,
            buyerId: buyer.Id,
            itemAssetId: assetId,
            buyerTradeUrl: TradeUrlFor(buyer.SteamId));

        return (seller, transactionId, BuildAuthenticatedClient(seller.Id, seller.SteamId));
    }

    private async Task<Guid> BuyerIdOfAsync(Guid transactionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transactionId)).BuyerId!.Value;
    }

    private async Task<User> UserByIdAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task SeedPaymentAddressAsync(Guid transactionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Skinora.Transactions.Domain.Entities.PaymentAddress>().Add(
            new Skinora.Transactions.Domain.Entities.PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = transactionId,
                Address = "TPaymentAddr1234567890abcdef1234",
                HdWalletIndex = 7,
                ExpectedAmount = 102m,
                ExpectedToken = Skinora.Shared.Enums.StablecoinType.USDT,
                MonitoringStatus = Skinora.Shared.Enums.MonitoringStatus.ACTIVE,
            });
        await db.SaveChangesAsync();
    }

    // ---------- T126: POST /transactions/:id/confirm-receipt (07 §7.6b) ----------

    [Fact]
    public async Task ConfirmReceipt_Unauthenticated_Returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmReceipt_Happy_Path_Returns_200_With_ItemDelivered()
    {
        var (buyer, transactionId, client) = await SetUpConfirmReceiptAsync();

        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        Assert.Equal("ITEM_DELIVERED", data.GetProperty("status").GetString());
        Assert.True(data.TryGetProperty("deliveryVerifiedAt", out _));
        // 07 §7.6b — the buyer's own confirmation is the whole evidence set here.
        Assert.Equal(
            new[] { "BUYER_CONFIRMED" },
            data.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()).ToArray());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = db.Set<Skinora.Transactions.Domain.Entities.Transaction>()
            .AsNoTracking().Single(t => t.Id == transactionId);
        Assert.Equal(Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.NotNull(persisted.DeliveryVerifiedAt);
        Assert.Equal(Skinora.Shared.Enums.DeliveryEvidence.BUYER_CONFIRMED, persisted.DeliveryEvidence);
        // The realtime relay's producer (03 §3.5 step 9); ITEM_DELIVERED has no
        // inbox notification type of its own.
        Assert.NotEmpty(db.Set<OutboxMessage>().AsNoTracking().ToList());
        _ = buyer;
    }

    [Fact]
    public async Task ConfirmReceipt_Repeat_Is_Idempotent_And_Returns_200()
    {
        var (_, transactionId, client) = await SetUpConfirmReceiptAsync();

        var first = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-receipt", content: null);
        var second = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstData = (await first.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        var secondData = (await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("data");
        // Same answer, not a fresh one: the repeat must not re-stamp.
        Assert.Equal(
            firstData.GetProperty("deliveryVerifiedAt").GetString(),
            secondData.GetProperty("deliveryVerifiedAt").GetString());
    }

    [Fact]
    public async Task ConfirmReceipt_Seller_Calling_Returns_403_NotAParty()
    {
        var buyer = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED,
            buyerId: buyer.Id,
            buyerTradeUrl: TradeUrlFor(buyer.SteamId));

        var client = BuildAuthenticatedClient(seller.Id, seller.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NOT_A_PARTY", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmReceipt_Before_Payment_Returns_409()
    {
        var buyer = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id, targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.SELLER_CONFIRMED,
            buyerId: buyer.Id,
            buyerTradeUrl: TradeUrlFor(buyer.SteamId));

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{transactionId:D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_STATE_TRANSITION",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConfirmReceipt_Unknown_Transaction_Returns_404()
    {
        var buyer = await _factory.CreateUserAsync();

        var client = BuildAuthenticatedClient(buyer.Id, buyer.SteamId);
        var response = await client.PostAsync(
            $"/api/v1/transactions/{Guid.NewGuid():D}/confirm-receipt", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("TRANSACTION_NOT_FOUND",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    private async Task<(User Buyer, Guid TransactionId, HttpClient Client)> SetUpConfirmReceiptAsync()
    {
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var transactionId = await _factory.SeedTransactionAsync(
            seller.Id,
            targetBuyerSteamId: buyer.SteamId,
            status: Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED,
            buyerId: buyer.Id,
            buyerTradeUrl: TradeUrlFor(buyer.SteamId));

        return (buyer, transactionId, BuildAuthenticatedClient(buyer.Id, buyer.SteamId));
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
    /// register tradeable items via <see cref="Register"/>; an unregistered
    /// asset reads as "inventory readable, asset absent" so the controller
    /// maps to <c>ITEM_NOT_IN_INVENTORY</c>. T121 — set
    /// <see cref="ForcedVisibility"/> to exercise the two non-readable
    /// outcomes (08 §2.3), which map to different responses.
    /// </summary>
    private sealed class FakeSteamInventoryReader : ISteamInventoryReader
    {
        private readonly Dictionary<(string steamId, string assetId), InventoryItemSnapshot> _items = [];

        public InventoryVisibility? ForcedVisibility { get; set; }

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

        /// <summary>
        /// T123 — forces the buyer-baseline capture to a non-readable outcome
        /// independently of <see cref="ForcedVisibility"/>: the seller's
        /// inventory and the buyer's are different people's profiles, and
        /// 03 §2.3 requires an unreadable BUYER inventory to be non-blocking.
        /// </summary>
        public InventoryVisibility? ForcedBaselineVisibility { get; set; }

        /// <summary>T123 — freshness of each item read, in order (07 §7.6a).</summary>
        public List<InventoryReadFreshness> ItemReadFreshness { get; } = [];

        public Task<InventoryLookupResult> GetItemAsync(
            string steamId64, string itemAssetId,
            InventoryReadFreshness freshness, CancellationToken cancellationToken)
        {
            ItemReadFreshness.Add(freshness);

            if (ForcedVisibility is InventoryVisibility.Private)
                return Task.FromResult(InventoryLookupResult.Private);
            if (ForcedVisibility is InventoryVisibility.Unavailable)
                return Task.FromResult(InventoryLookupResult.Unavailable);

            return Task.FromResult(_items.TryGetValue((steamId64, itemAssetId), out var item)
                ? InventoryLookupResult.Found(item)
                : InventoryLookupResult.NotFound);
        }

        public Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
            string steamId64, string classId, string? instanceId,
            InventoryReadFreshness freshness, CancellationToken cancellationToken)
        {
            if (ForcedBaselineVisibility is InventoryVisibility.Private)
                return Task.FromResult(InventoryClassBaselineResult.Private);
            if (ForcedBaselineVisibility is InventoryVisibility.Unavailable)
                return Task.FromResult(InventoryClassBaselineResult.Unavailable);

            var assets = _items
                .Where(kv => kv.Key.steamId == steamId64
                    && string.Equals(kv.Value.ClassId, classId, StringComparison.Ordinal)
                    && (instanceId is null
                        || string.Equals(kv.Value.InstanceId, instanceId, StringComparison.Ordinal)))
                .Select(kv => new InventoryClassAsset(kv.Value.AssetId, kv.Value.AssetProperties))
                .OrderBy(a => a.AssetId, StringComparer.Ordinal)
                .ToList();

            var inventoryClassIds = _items
                .Where(kv => kv.Key.steamId == steamId64)
                .Select(kv => kv.Value.ClassId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(
                InventoryClassBaselineResult.Captured(assets, inventoryClassIds));
        }

        /// <summary>T130 — inventory-wide projection of the same registry.</summary>
        public Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
            string steamId64,
            InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
        {
            if (ForcedBaselineVisibility is InventoryVisibility.Private)
                return Task.FromResult(InventoryFingerprintResult.Private);
            if (ForcedBaselineVisibility is InventoryVisibility.Unavailable)
                return Task.FromResult(InventoryFingerprintResult.Unavailable);

            var assets = _items
                .Where(kv => kv.Key.steamId == steamId64)
                .Select(kv => new InventoryFingerprintEntry(
                    kv.Value.AssetId, kv.Value.ClassId, kv.Value.InstanceId, kv.Value.Name))
                .OrderBy(a => a.AssetId, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(InventoryFingerprintResult.Captured(assets));
        }
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

        /// <summary>
        /// T121 — forces the seller's inventory read to a non-readable 08 §2.3
        /// outcome. Defaults to <c>null</c> (readable); tests that flip it reset
        /// it in a finally block, matching the <see cref="TradeHold"/> stub's
        /// convention.
        /// </summary>
        public InventoryVisibility? InventoryVisibilityOverride
        {
            get => _inventory.ForcedVisibility;
            set => _inventory.ForcedVisibility = value;
        }

        /// <summary>
        /// T123 — forces the BUYER-baseline capture to a non-readable outcome,
        /// separately from <see cref="InventoryVisibilityOverride"/>: they are
        /// two different people's profiles, and 03 §2.3 requires an unreadable
        /// buyer inventory to be non-blocking.
        /// </summary>
        public InventoryVisibility? BaselineVisibilityOverride
        {
            get => _inventory.ForcedBaselineVisibility;
            set => _inventory.ForcedBaselineVisibility = value;
        }

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
            Guid? buyerId = null,
            // T123 — confirm-ready re-reads the listed asset, so a test that
            // drives it has to pin the id it registered in the fake inventory.
            string? itemAssetId = null,
            string? buyerTradeUrl = null)
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
                BuyerTradeUrl = buyerId is null ? null : (buyerTradeUrl ?? TradeUrlFor(SteamId)),
                // 06 §3.5 — NOT NULL from ACCEPTED onwards. Restricted to the
                // post-accept forward states so the CREATED/FLAGGED fixtures
                // keep their existing shape.
                AcceptedAt = status is Skinora.Shared.Enums.TransactionStatus.ACCEPTED
                    or Skinora.Shared.Enums.TransactionStatus.SELLER_CONFIRMED
                    or Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED
                    or Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                    or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-5)
                    : null,
                SellerConfirmDeadline = status == Skinora.Shared.Enums.TransactionStatus.ACCEPTED
                    ? nowUtc.AddHours(1)
                    : null,
                PaymentReceivedAt =
                    status is Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED
                        or Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                        or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                        ? DateTime.UtcNow.AddMinutes(-5)
                        : null,
                ItemAssetId = itemAssetId ?? Guid.NewGuid().ToString("N")[..12],
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
                // T126 — 06 §3.5 brackets SellerReadyConfirmedAt exactly like
                // AcceptedAt one state later, and the DeliverItem guard reads it
                // through HasFieldsForSellerConfirmed(). A PAYMENT_RECEIVED
                // fixture without it could never deliver.
                SellerReadyConfirmedAt = status is Skinora.Shared.Enums.TransactionStatus.SELLER_CONFIRMED
                    or Skinora.Shared.Enums.TransactionStatus.PAYMENT_RECEIVED
                    or Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                    or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-4)
                    : null,
                // The trail a delivered transaction necessarily carries: the
                // DeliverItem guard requires DeliveryVerifiedAt and its OnEntry
                // stamps ItemDeliveredAt.
                DeliveryVerifiedAt = status is Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                    or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-2)
                    : null,
                ItemDeliveredAt = status is Skinora.Shared.Enums.TransactionStatus.ITEM_DELIVERED
                    or Skinora.Shared.Enums.TransactionStatus.COMPLETED
                    ? nowUtc.AddMinutes(-2)
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
            // T123 — same class of dependency, same reason: PaymentAddress FKs
            // to Transaction with NO ACTION, so a leftover row makes the next
            // Reset() fail on "FOREIGN KEY constraint failed" and takes the
            // whole class down with it. IgnoreQueryFilters + ExecuteDelete
            // because the entity is ISoftDeletable: RemoveRange would only
            // stamp IsDeleted and the row would still hold the FK.
            db.Set<Skinora.Transactions.Domain.Entities.PaymentAddress>()
                .IgnoreQueryFilters().ExecuteDelete();
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
