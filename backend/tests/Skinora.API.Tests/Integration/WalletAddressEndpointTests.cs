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
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.Session;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Wallet;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class WalletAddressEndpointTests : IClassFixture<WalletAddressEndpointTests.Factory>
{
    private const string TestSecret = "wallet-test-secret-key-minimum-32-chars!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamIdPrefix = "76561198333444";
    // Real base58check TRON addresses (T146 — the validator checks the
    // checksum and the 0x41 version byte); generated with tronweb 5.3.5
    // `TronWeb.address.fromHex("41" + sha256("skinora-t146-N")[0..40])`.
    private const string ValidSellerAddress = "TMaGysihDKhVGCzCurkbLzki2abuS9nVVr";
    private const string ValidSecondSellerAddress = "TNQQeunDpRHZUycJiQXjne8GQHnWKnNutN";
    private const string ValidRefundAddress = "TAn8FKbxAF8SXQDht8KMtdhpK1ZD9aTRPF";
    private const string UnrelatedSellerAddress = "TGcjPRe5Cw8ASf2CDwK6Yxf3DGNRNtYoWu";

    // EIP-55 specification examples (eips.ethereum.org/EIPS/eip-55) —
    // mixed case, checksum valid.
    private const string EthChecksummed = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";
    private const string EthChecksummedLower = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string EthChecksummed2 = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";
    private const string EthChecksummed2Lower = "0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public WalletAddressEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- format validation ----------

    [Fact]
    public async Task UpdateSellerWallet_InvalidFormat_Returns400InvalidWalletAddress()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        // Missing 'T' prefix → INVALID_WALLET_ADDRESS.
        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = "XYz1234567890abcdef1234567890abcde",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("INVALID_WALLET_ADDRESS", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSellerWallet_WrongLength_Returns400InvalidWalletAddress()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = "TShort",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_WALLET_ADDRESS", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- first-time save (no existing address) ----------

    [Fact]
    public async Task UpdateSellerWallet_NoExistingAddress_ReturnsSuccessAndPersists()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(ValidSellerAddress, data.GetProperty("walletAddress").GetString());
        Assert.Equal(0, data.GetProperty("activeTransactionsUsingOldAddress").GetInt32());

        // T146 — the body carries no network, so the address is TRON (07 §1.3
        // backward compatibility) and the response says so.
        Assert.Equal("TRON", data.GetProperty("network").GetString());
        Assert.True(data.GetProperty("checksumVerified").GetBoolean());

        // Persistence: one TRON PAYOUT row, address + ChangedAt populated.
        var persisted = Assert.Single(await _factory.GetWalletsAsync(user.Id));
        Assert.Equal(BlockchainNetwork.TRON, persisted.Network);
        Assert.Equal(WalletAddressType.PAYOUT, persisted.Type);
        Assert.Equal(ValidSellerAddress, persisted.Address);
        Assert.NotNull(persisted.ChangedAt);
    }

    // ---------- re-auth enforcement (existing address) ----------

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithoutReAuthToken_Returns403ReAuthRequired()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_REQUIRED", body.GetProperty("error").GetProperty("code").GetString());

        // No mutation on failure path.
        var persisted = await _factory.GetWalletAsync(user.Id, BlockchainNetwork.TRON, WalletAddressType.PAYOUT);
        Assert.Equal(ValidSellerAddress, persisted!.Address);
    }

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithInvalidReAuthToken_Returns403TokenInvalid()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", "garbage-token");

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_TOKEN_INVALID", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateSellerWallet_ExistingAddress_WithValidReAuthToken_Persists()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
        });
        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Token is single-use — the next attempt with the same token fails.
        var reuse = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });
        Assert.Equal(HttpStatusCode.Forbidden, reuse.StatusCode);
        var body = await reuse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_TOKEN_INVALID", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- sanctions ----------

    [Fact]
    public async Task UpdateSellerWallet_SanctionsMatch_Returns403AndLeavesAddressUnchanged()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        _factory.SanctionsStub.ShouldMatch = true;

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("SANCTIONS_MATCH", body.GetProperty("error").GetProperty("code").GetString());

        Assert.Empty(await _factory.GetWalletsAsync(user.Id));
        // T146 — screened on the address's own network.
        Assert.Equal(BlockchainNetwork.TRON, _factory.SanctionsStub.LastNetwork);
    }

    // ---------- activeTransactionsUsingOldAddress count ----------

    [Fact]
    public async Task UpdateSellerWallet_CountsNonTerminalTransactionsOnly()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
        });

        // Two non-terminal (CREATED, PAYMENT_RECEIVED) + two terminal
        // (COMPLETED, REFUNDED) using ValidSellerAddress as seller payout
        // snapshot. One more transaction uses a different address and must not
        // be counted.
        //
        // REFUNDED is the regression case (T133a-ActiveCounterRefunded): it is
        // terminal (05 §4.1) but ActiveTransactionCounter's exclusion list only
        // carried the five pre-v3.0 terminals, so a user whose buyer-favour
        // refund had already settled kept being told they had an active
        // transaction on the old address (02 §12.3). This test would have
        // stayed green throughout, because it never exercised REFUNDED.
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.CREATED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.PAYMENT_RECEIVED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.COMPLETED;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
            t.Status = TransactionStatus.REFUNDED;
            // REFUNDED reuses the cancellation trail (05 §4.2);
            // CK_Transactions_Cancel rejects the row without it.
            t.CancelledBy = CancelledByType.ADMIN;
            t.CancelledAt = DateTime.UtcNow.AddMinutes(-5);
            t.CancelReason = "Buyer-favour dispute resolution";
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = UnrelatedSellerAddress;
            t.Status = TransactionStatus.CREATED;
        });

        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(2, data.GetProperty("activeTransactionsUsingOldAddress").GetInt32());
    }

    // ---------- refund endpoint ----------

    [Fact]
    public async Task UpdateRefundWallet_NoExistingAddress_PersistsAndSetsTimestamp()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/refund", new
        {
            walletAddress = ValidRefundAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal(ValidRefundAddress, data.GetProperty("walletAddress").GetString());

        // Only the TRON REFUND row; no payout row.
        var persisted = Assert.Single(await _factory.GetWalletsAsync(user.Id));
        Assert.Equal(WalletAddressType.REFUND, persisted.Type);
        Assert.Equal(BlockchainNetwork.TRON, persisted.Network);
        Assert.Equal(ValidRefundAddress, persisted.Address);
        Assert.NotNull(persisted.ChangedAt);
    }

    // ---------- auth ----------

    [Fact]
    public async Task UpdateSellerWallet_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- suspended-account enforcement (T105a AC4) ----------

    [Fact]
    public async Task UpdateSellerWallet_SuspendedUser_IsRejected_AndLeavesAddressUnchanged()
    {
        // T105a AC4 — a suspended user cannot change payout/refund addresses.
        // The guard lives in WalletAddressService's user lookup
        // (!IsDeactivated && !IsSuspended) → UserNotFound → 401. Mirrors the
        // create/accept/cancel suspended-rejection tests so every fund-flow
        // mutation guard has a negative test.
        var user = await _factory.CreateUserAsync(u => u.IsSuspended = true);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // No mutation on the rejected path.
        Assert.Empty(await _factory.GetWalletsAsync(user.Id));
    }

    // ---------- T105b: wallet address history ----------

    [Fact]
    public async Task UpdateSellerWallet_FirstTimeSet_WritesNoHistory()
    {
        // A first-ever set (previous is null) records nothing — only an actual
        // replacement produces a previous-address row.
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _factory.GetWalletHistoryAsync(user.Id));
    }

    [Fact]
    public async Task UpdateSellerWallet_Replacement_WritesPreviousAddressToHistory()
    {
        var setAt = DateTime.UtcNow.AddDays(-10);
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress, setAt));
        });
        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Exactly one history row recording the SUPERSEDED address + its set date.
        var row = Assert.Single(await _factory.GetWalletHistoryAsync(user.Id));
        Assert.Equal("seller", row.Type);
        Assert.Equal(ValidSellerAddress, row.Address);
        // T145/T146 (06 §1.4) — the history is kept per network; a body
        // without a network is a TRON address.
        Assert.Equal(BlockchainNetwork.TRON, row.Network);
        Assert.NotNull(row.SetAt);
        Assert.Equal(setAt, row.SetAt!.Value, TimeSpan.FromSeconds(1));
        Assert.NotEqual(default, row.CreatedAt);

        // The new address is current on the TRON PAYOUT row, not in history.
        var persisted = await _factory.GetWalletAsync(user.Id, BlockchainNetwork.TRON, WalletAddressType.PAYOUT);
        Assert.Equal(ValidSecondSellerAddress, persisted!.Address);
    }

    [Fact]
    public async Task UpdateSellerWallet_TwoReplacements_AppendsTwoRowsOldestFirst()
    {
        // Append-only: each replacement INSERTs; prior rows are never updated.
        var user = await _factory.CreateUserAsync(u =>
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress)));
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/v1/users/me/wallet/seller", new { walletAddress = ValidSecondSellerAddress }))
            .StatusCode);

        client.DefaultRequestHeaders.Remove("X-ReAuth-Token");
        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/v1/users/me/wallet/seller", new { walletAddress = UnrelatedSellerAddress }))
            .StatusCode);

        var history = await _factory.GetWalletHistoryAsync(user.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(ValidSellerAddress, history[0].Address);
        Assert.Equal(ValidSecondSellerAddress, history[1].Address);
        Assert.All(history, h => Assert.Equal("seller", h.Type));
    }

    [Fact]
    public async Task UpdateRefundWallet_Replacement_WritesBuyerRow_SellerTrailUntouched()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.TRON, WalletAddressType.REFUND, ValidRefundAddress, DateTime.UtcNow.AddDays(-2)));
        });
        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/refund", new
        {
            walletAddress = ValidSecondSellerAddress,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = Assert.Single(await _factory.GetWalletHistoryAsync(user.Id));
        Assert.Equal("buyer", row.Type);
        Assert.Equal(ValidRefundAddress, row.Address);
    }

    [Fact]
    public async Task UpdateSellerWallet_SuspendedUser_WritesNoHistory()
    {
        // The suspended guard rejects before persistence, so no history is written.
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
            u.IsSuspended = true;
        });
        var token = await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId);
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add("X-ReAuth-Token", token);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSecondSellerAddress,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await _factory.GetWalletHistoryAsync(user.Id));
    }

    // ---------- T146: per-network addresses (07 §1.3, 06 §1.4) ----------

    [Fact]
    public async Task UpdateSellerWallet_EthereumChecksummed_PersistsLowercase_EchoesEip55()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed,
            network = "ETHEREUM",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(EthChecksummed, data.GetProperty("walletAddress").GetString());
        Assert.Equal("ETHEREUM", data.GetProperty("network").GetString());
        Assert.True(data.GetProperty("checksumVerified").GetBoolean());

        // Stored in the canonical lowercase form (06 §1.4), screened on Ethereum.
        var row = Assert.Single(await _factory.GetWalletsAsync(user.Id));
        Assert.Equal(BlockchainNetwork.ETHEREUM, row.Network);
        Assert.Equal(EthChecksummedLower, row.Address);
        Assert.Equal(BlockchainNetwork.ETHEREUM, _factory.SanctionsStub.LastNetwork);
        Assert.Equal(EthChecksummedLower, _factory.SanctionsStub.LastAddress);
    }

    [Theory]
    // All lowercase — a typo cannot be detected (EIP-55 spec "all lower" examples).
    [InlineData("0xde709f2102306220921060314715629080e2fb77")]
    [InlineData("0x27b1fdb04752bbc536007a920d24acb045561c26")]
    // All uppercase. These two are the EIP-55 spec "all caps" examples whose
    // checksum happens to be all caps: a single-case input carries no
    // evidence of a checksum, so they are treated like any other single-case
    // address (T145 decision iv) and need the confirmation too.
    [InlineData("0x52908400098527886E0F7030069857D2E4169EE7")]
    [InlineData("0x8617E340B3D01FA5F11F306F4090FD50E238070D")]
    public async Task UpdateSellerWallet_EthereumSingleCase_WithoutConfirmation_Returns400ConfirmationRequired(string address)
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = address,
            network = "ETHEREUM",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("error");
        Assert.Equal("INVALID_WALLET_ADDRESS", error.GetProperty("code").GetString());
        var details = error.GetProperty("details");
        Assert.Equal("CHECKSUM_CONFIRMATION_REQUIRED", details.GetProperty("reason").GetString());
        Assert.True(details.GetProperty("requiresConfirmation").GetBoolean());
        Assert.Equal("ETHEREUM", details.GetProperty("network").GetString());
        Assert.Empty(await _factory.GetWalletsAsync(user.Id));
    }

    [Fact]
    public async Task UpdateSellerWallet_EthereumLowercase_WithConfirmation_Persists_ChecksumNotVerified()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed2Lower,
            network = "ETHEREUM",
            confirmLowercase = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(data.GetProperty("checksumVerified").GetBoolean());
        // Echoed in display form — the user sees the checksummed spelling.
        Assert.Equal(EthChecksummed2, data.GetProperty("walletAddress").GetString());
        Assert.Equal(EthChecksummed2Lower,
            (await _factory.GetWalletAsync(user.Id, BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT))!.Address);
    }

    [Theory]
    // Ethereum: one letter's case flipped in an EIP-55 example.
    [InlineData("ETHEREUM", "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD", "CHECKSUM_MISMATCH")]
    // Ethereum: wrong length, non-hex, a TRON address under the Ethereum network.
    [InlineData("ETHEREUM", "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAe", "INVALID_FORMAT")]
    [InlineData("ETHEREUM", "0xZZAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", "INVALID_FORMAT")]
    [InlineData("ETHEREUM", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "INVALID_FORMAT")]
    // TRON: right shape, last character changed — base58check fails.
    [InlineData("TRON", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6u", "CHECKSUM_MISMATCH")]
    // TRON: valid base58check over version byte 0x42 (not 0x41) — still 34
    // chars starting with 'T', not a TRON address (tronweb isAddress = false).
    [InlineData("TRON", "TpSyGx2w2bR9GdrDrYtrYwbvSPhwSmg7ER", "CHECKSUM_MISMATCH")]
    // TRON: an Ethereum address under the TRON network.
    [InlineData("TRON", "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", "INVALID_FORMAT")]
    public async Task UpdateSellerWallet_InvalidForNetwork_Returns400WithReason(
        string network, string address, string reason)
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = address,
            network,
            confirmLowercase = true, // a confirmation never rescues an invalid address
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("error");
        Assert.Equal("INVALID_WALLET_ADDRESS", error.GetProperty("code").GetString());
        Assert.Equal(reason, error.GetProperty("details").GetProperty("reason").GetString());
        Assert.False(error.GetProperty("details").GetProperty("requiresConfirmation").GetBoolean());
        Assert.Empty(await _factory.GetWalletsAsync(user.Id));
    }

    [Theory]
    [InlineData("BITCOIN")]
    [InlineData("tron")]        // enum names only, case-sensitive (07 §1.3)
    [InlineData("TRC-20")]      // the pre-F8 display value is not a network
    public async Task UpdateSellerWallet_UnknownNetwork_Returns400ValidationError(string network)
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = ValidSellerAddress,
            network,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(await _factory.GetWalletsAsync(user.Id));
    }

    [Fact]
    public async Task UpdateSellerWallet_FirstAddressOnSecondNetwork_NeedsNoReAuth_AndLeavesTronUntouched()
    {
        // Re-auth is per network (07 §1.3): the TRON payout address exists,
        // but the Ethereum one is a first-time set.
        var tronSetAt = DateTime.UtcNow.AddDays(-30);
        var user = await _factory.CreateUserAsync(u =>
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress, tronSetAt)));
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed,
            network = "ETHEREUM",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tron = await _factory.GetWalletAsync(user.Id, BlockchainNetwork.TRON, WalletAddressType.PAYOUT);
        Assert.Equal(ValidSellerAddress, tron!.Address);
        // The TRON cooldown clock did not restart (02 §12.3 per network).
        Assert.Equal(tronSetAt, tron.ChangedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Empty(await _factory.GetWalletHistoryAsync(user.Id));

        // Changing the Ethereum address now needs re-auth.
        var change = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed2,
            network = "ETHEREUM",
        });
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        var body = await change.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("RE_AUTH_REQUIRED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateRefundWallet_EthereumReplacement_WritesEthereumHistory_TronUntouched()
    {
        var ethSetAt = DateTime.UtcNow.AddDays(-4);
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.TRON, WalletAddressType.REFUND, ValidRefundAddress));
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.ETHEREUM, WalletAddressType.REFUND, EthChecksummedLower, ethSetAt));
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/refund", new
        {
            walletAddress = EthChecksummed2,
            network = "ETHEREUM",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = Assert.Single(await _factory.GetWalletHistoryAsync(user.Id));
        Assert.Equal("buyer", row.Type);
        Assert.Equal(BlockchainNetwork.ETHEREUM, row.Network);
        Assert.Equal(EthChecksummedLower, row.Address);
        Assert.Equal(ethSetAt, row.SetAt!.Value, TimeSpan.FromSeconds(1));

        Assert.Equal(EthChecksummed2Lower,
            (await _factory.GetWalletAsync(user.Id, BlockchainNetwork.ETHEREUM, WalletAddressType.REFUND))!.Address);
        Assert.Equal(ValidRefundAddress,
            (await _factory.GetWalletAsync(user.Id, BlockchainNetwork.TRON, WalletAddressType.REFUND))!.Address);
    }

    [Fact]
    public async Task UpdateSellerWallet_SameAddressInOtherLetterCase_IsNotAReplacement()
    {
        // 06 §1.4 — the same Ethereum address in another spelling is the same
        // address: no history row, no "active transactions on the old address".
        var user = await _factory.CreateUserAsync(u =>
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthChecksummedLower)));
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed,
            network = "ETHEREUM",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await _factory.GetWalletHistoryAsync(user.Id));
        Assert.Single(await _factory.GetWalletsAsync(user.Id));
    }

    [Fact]
    public async Task UpdateSellerWallet_ConfirmationRequired_DoesNotConsumeTheReAuthToken()
    {
        // The re-auth token is single-use. A request answered with "please
        // confirm this unchecksummed address" must not burn it — the retry
        // with confirmLowercase carries the same token.
        var user = await _factory.CreateUserAsync(u =>
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthChecksummedLower)));
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));

        var first = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed2Lower,
            network = "ETHEREUM",
        });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);

        var retry = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed2Lower,
            network = "ETHEREUM",
            confirmLowercase = true,
        });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(EthChecksummed2Lower,
            (await _factory.GetWalletAsync(user.Id, BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT))!.Address);
    }

    [Fact]
    public async Task UpdateSellerWallet_EthereumChange_CountsOnlyTransactionsOnTheOldEthereumAddress()
    {
        var user = await _factory.CreateUserAsync(u =>
        {
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.TRON, WalletAddressType.PAYOUT, ValidSellerAddress));
            u.WalletAddresses.Add(UserWalletAddress.For(
                BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EthChecksummedLower));
        });
        // One live transaction on the TRON address, one on the Ethereum address.
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = ValidSellerAddress;
        });
        await _factory.CreateTransactionAsync(t =>
        {
            t.SellerId = user.Id;
            t.SellerPayoutAddress = EthChecksummedLower;
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);
        client.DefaultRequestHeaders.Add(
            "X-ReAuth-Token", await _factory.IssueReAuthTokenAsync(user.Id, user.SteamId));

        var response = await client.PutAsJsonAsync("/api/v1/users/me/wallet/seller", new
        {
            walletAddress = EthChecksummed2,
            network = "ETHEREUM",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(1, data.GetProperty("activeTransactionsUsingOldAddress").GetInt32());
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

    public sealed class ConfigurableSanctionsStub : IWalletSanctionsCheck
    {
        public bool ShouldMatch { get; set; }
        public string MatchedList { get; set; } = "OFAC";
        public BlockchainNetwork? LastNetwork { get; set; }
        public string? LastAddress { get; set; }

        public Task<WalletSanctionsDecision> EvaluateAsync(
            BlockchainNetwork network, string walletAddress, CancellationToken cancellationToken)
        {
            LastNetwork = network;
            LastAddress = walletAddress;
            return Task.FromResult(ShouldMatch
                ? WalletSanctionsDecision.Match(MatchedList)
                : WalletSanctionsDecision.NoMatch());
        }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private int _transactionSuffix;

        public ConfigurableSanctionsStub SanctionsStub { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = "WalletTester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Transaction> CreateTransactionAsync(Action<Transaction>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _transactionSuffix);
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = TransactionStatus.CREATED,
                SellerId = Guid.NewGuid(),
                // Pair STEAM_ID method with TargetBuyerSteamId (InviteToken NULL)
                // to satisfy CK_Transactions_BuyerMethod_SteamId.
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                TargetBuyerSteamId = $"765611999{suffix:D8}",
                ItemAssetId = $"asset-{suffix}",
                ItemClassId = $"class-{suffix}",
                ItemName = $"Item {suffix}",
                StablecoinType = StablecoinType.USDT,
                Price = 10m,
                CommissionRate = 0.02m,
                CommissionAmount = 0.2m,
                TotalAmount = 10.2m,
                // Transaction snapshot placeholder (a real TRON address).
                SellerPayoutAddress = "TSEBLSqEXdfAMfE8jJsLfDuskpVM2AhqhV",
                PaymentTimeoutMinutes = 60,
                CreatedAt = DateTime.UtcNow,
            };
            customize?.Invoke(tx);

            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return tx;
        }

        public async Task<User> GetUserAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == userId);
        }

        public async Task<List<UserWalletAddress>> GetWalletsAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<UserWalletAddress>()
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .ToListAsync();
        }

        public async Task<UserWalletAddress?> GetWalletAsync(
            Guid userId, BlockchainNetwork network, WalletAddressType type)
            => (await GetWalletsAsync(userId))
                .SingleOrDefault(w => w.Network == network && w.Type == type);

        public async Task<List<WalletAddressHistory>> GetWalletHistoryAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<WalletAddressHistory>()
                .AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.Id)
                .ToListAsync();
        }

        public async Task<string> IssueReAuthTokenAsync(Guid userId, string steamId)
        {
            using var scope = Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IReAuthTokenStore>();
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await store.IssueAsync(
                token,
                new ReAuthTokenPayload(userId, steamId),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            return token;
        }

        public void Reset()
        {
            SanctionsStub.ShouldMatch = false;
            SanctionsStub.LastNetwork = null;
            SanctionsStub.LastAddress = null;

            // Full schema rebuild — T82 wallet sanctions match path now stages
            // a FraudFlag + EMERGENCY_HOLD cascade + AuditLog rows when the
            // stub returns Match=true. Granular RemoveRange across that chain
            // is fragile (FK + RowVersion). EnsureDeleted + EnsureCreated
            // restores the SqliteConnection's in-memory schema with seed data
            // in <50ms.
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
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

                // T34 — in-memory re-auth store (no Redis) + per-test sanctions stub.
                services.RemoveAll<IReAuthTokenStore>();
                services.AddSingleton<IReAuthTokenStore>(sp =>
                    new InMemoryReAuthTokenStore(sp.GetRequiredService<TimeProvider>()));

                services.RemoveAll<IWalletSanctionsCheck>();
                services.AddSingleton<IWalletSanctionsCheck>(SanctionsStub);
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
