using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Tests.Integration.Timeouts;

/// <summary>
/// Test double for <see cref="IBackgroundJobScheduler"/>. Captures every
/// call so a test can assert "the service scheduled a payment timeout with
/// delay X" without spinning up Hangfire. <c>Delete</c> failure can be
/// simulated by toggling <see cref="DeleteSucceeds"/>.
/// </summary>
internal sealed class CapturingJobScheduler : IBackgroundJobScheduler
{
    public List<ScheduledCall> ScheduledCalls { get; } = new();
    public List<EnqueuedCall> EnqueuedCalls { get; } = new();
    public List<string> DeletedJobIds { get; } = new();
    public bool DeleteSucceeds { get; set; } = true;

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        var jobId = Guid.NewGuid().ToString("N");
        ScheduledCalls.Add(new ScheduledCall(typeof(T), methodCall, delay, jobId));
        return jobId;
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        var jobId = Guid.NewGuid().ToString("N");
        EnqueuedCalls.Add(new EnqueuedCall(typeof(T), methodCall, jobId));
        return jobId;
    }

    public bool Delete(string jobId)
    {
        DeletedJobIds.Add(jobId);
        return DeleteSucceeds;
    }

    public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }

    internal sealed record ScheduledCall(Type TargetType, LambdaExpression Expression, TimeSpan Delay, string JobId);

    internal sealed record EnqueuedCall(Type TargetType, LambdaExpression Expression, string JobId);
}

/// <summary>
/// In-memory <see cref="IOutboxService"/> capturing every published event so a
/// test can assert "publisher emitted X" without spinning up the real outbox
/// table writer. Mirrors <see cref="CapturingJobScheduler"/>.
/// </summary>
internal sealed class CapturingOutboxService : IOutboxService
{
    public List<IDomainEvent> Published { get; } = new();

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Default no-op <see cref="ITimeoutSideEffectPublisher"/> for tests that only
/// care about the executor / scanner state-transition semantics and not the
/// outbox fan-out.
/// </summary>
internal sealed class NoOpTimeoutSideEffectPublisher : ITimeoutSideEffectPublisher
{
    public Task PublishAsync(
        Transaction transaction,
        TransactionStatus previousStatus,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Helpers for setting up Transaction + SystemSetting fixtures that the
/// T47 timeout tests rely on. Centralized to avoid copy-paste across the
/// six test classes in this namespace.
/// </summary>
internal static class TimeoutTestFixtures
{
    public const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    public const string SellerSteamId = "76561198900000001";

    /// <summary>
    /// Inserts a User with a unique steamId and returns it. The post-CREATED
    /// states (TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, ...) require
    /// <c>BuyerId</c> NOT NULL (06 §3.5), and the FK enforces the buyer row
    /// exists, so every test that exercises those states must seed one.
    /// </summary>
    public static async Task<Skinora.Users.Domain.Entities.User> AddBuyerAsync(AppDbContext context)
    {
        var buyer = new Skinora.Users.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            SteamId = "7656" + Random.Shared.NextInt64(1_000_000_000_000L, 9_999_999_999_999L),
            SteamDisplayName = "Buyer-" + Guid.NewGuid().ToString("N")[..6],
        };
        context.Set<Skinora.Users.Domain.Entities.User>().Add(buyer);
        await context.SaveChangesAsync();
        return buyer;
    }

    public static async Task ConfigureSettingAsync(AppDbContext context, string key, string value)
    {
        var existing = await context.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
        {
            context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                IsConfigured = true,
                DataType = "decimal",
                Category = "Test",
            });
        }
        else
        {
            existing.Value = value;
            existing.IsConfigured = true;
        }
        await context.SaveChangesAsync();
    }

    public static Transaction NewTransaction(
        Guid sellerId,
        TransactionStatus status,
        DateTime nowUtc,
        DateTime? acceptDeadline = null,
        DateTime? tradeOfferToSellerDeadline = null,
        DateTime? paymentDeadline = null,
        DateTime? tradeOfferToBuyerDeadline = null,
        bool isOnHold = false,
        DateTime? timeoutFrozenAt = null,
        string? paymentTimeoutJobId = null,
        string? timeoutWarningJobId = null,
        Guid? buyerId = null,
        string? buyerRefundAddress = null,
        int paymentTimeoutMinutes = 1440)
        => new()
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerRefundAddress = buyerRefundAddress,
            BuyerIdentificationMethod = BuyerIdentificationMethod.OPEN_LINK,
            InviteToken = "tok-" + Guid.NewGuid().ToString("N")[..8],
            ItemAssetId = "100200300",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = paymentTimeoutMinutes,
            AcceptDeadline = acceptDeadline,
            TradeOfferToSellerDeadline = tradeOfferToSellerDeadline,
            PaymentDeadline = paymentDeadline,
            TradeOfferToBuyerDeadline = tradeOfferToBuyerDeadline,
            IsOnHold = isOnHold,
            TimeoutFrozenAt = timeoutFrozenAt,
            PaymentTimeoutJobId = paymentTimeoutJobId,
            TimeoutWarningJobId = timeoutWarningJobId,
        };

    /// <summary>
    /// Default <see cref="ITimeoutSideEffectPublisher"/> for tests that focus on
    /// state transitions / no-op semantics and do not care about the outbox
    /// fan-out. Tests asserting publisher behaviour should construct a
    /// <see cref="TimeoutSideEffectPublisher"/> with a
    /// <see cref="CapturingOutboxService"/> directly.
    /// </summary>
    public static ITimeoutSideEffectPublisher NoOpSideEffects() => new NoOpTimeoutSideEffectPublisher();

    public static IOptions<TimeoutSchedulingOptions> Options(
        int scannerSeconds = 30,
        int batchSize = 200,
        int recoveryThresholdSeconds = 60)
        => Microsoft.Extensions.Options.Options.Create(new TimeoutSchedulingOptions
        {
            DeadlineScannerIntervalSeconds = scannerSeconds,
            DeadlineScannerBatchSize = batchSize,
            HeartbeatIntervalSeconds = 30,
            RecoveryThresholdSeconds = recoveryThresholdSeconds,
        });
}

