using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>Shared fixtures for the T145 network-dimension tests (06 §1.4).</summary>
internal static class NetworkDimensionTestSupport
{
    /// <summary>
    /// Tether USDT on TRON and on Ethereum — real addresses of both shapes, so
    /// the canonicalisation rule is exercised on genuine base58 and EIP-55 input.
    /// </summary>
    public const string TronAddress = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    public const string EvmChecksummed = "0xdAC17F958D2ee523a2206206994597C13D831ec7";
    public const string EvmLower = "0xdac17f958d2ee523a2206206994597c13d831ec7";
    public const string EvmChecksummed2 = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";
    public const string EvmLower2 = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";

    public const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    /// <summary>Every module must be on the model so <c>MigrateAsync</c> sees no drift.</summary>
    public static void RegisterAllModules()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
        AdminModuleDbRegistration.RegisterAdminModule();
        PaymentsModuleDbRegistration.RegisterPaymentsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    public static Transaction NewTransaction(
        Guid sellerId,
        TransactionStatus status = TransactionStatus.CREATED,
        BlockchainNetwork? network = null)
    {
        var nowUtc = DateTime.UtcNow;
        return new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000902",
            ItemAssetId = "A" + Guid.NewGuid().ToString("N")[..12],
            ItemClassId = "98765432101",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.000000m,
            CommissionRate = 0.0200m,
            CommissionAmount = 1.000000m,
            TotalAmount = 51.000000m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 60,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            Network = network,
        };
    }

    public static PaymentAddress NewPaymentAddress(
        Guid transactionId, BlockchainNetwork network, string address, int hdIndex)
        => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Network = network,
            Address = address,
            HdWalletIndex = hdIndex,
            ExpectedAmount = 51.000000m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        };

    public static BlockchainTransaction NewInbound(
        Guid transactionId, Guid paymentAddressId, BlockchainNetwork network,
        string? txHash, int? eventIndex = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = paymentAddressId,
            Network = network,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = txHash,
            EventIndex = eventIndex,
            FromAddress = network == BlockchainNetwork.TRON ? TronAddress : EvmChecksummed2,
            ToAddress = network == BlockchainNetwork.TRON ? SellerWallet : EvmChecksummed,
            Amount = 51.000000m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.DETECTED,
            CreatedAt = DateTime.UtcNow,
        };
}
