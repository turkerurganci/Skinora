using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Steam.Application.Recovery;

/// <summary>
/// Outcome of one <see cref="IBotRecoveryMaterialiser.TryMaterialiseAsync"/> call.
/// <see cref="Created"/> is false when a recovery row already existed (idempotent
/// no-op); <see cref="AutoHeld"/> is true only when an EMERGENCY_HOLD was applied
/// (false for already-held or terminal transactions).
/// </summary>
public readonly record struct BotRecoveryMaterialisation(bool Created, bool AutoHeld)
{
    /// <summary>A transaction that already had a recovery row — nothing staged.</summary>
    public static readonly BotRecoveryMaterialisation Skipped = new(Created: false, AutoHeld: false);
}

/// <summary>
/// Stages the recovery-queue entry + auto-EMERGENCY_HOLD for ONE stuck escrow on
/// a restricted/banned bot. Extracted so the event-driven sweep
/// (<see cref="BotRestrictionRecoveryConsumer"/>) and the boundary-race safety net
/// in the trade-accept webhook (<c>SteamWebhookHandler.AcceptEscrowAsync</c>,
/// T103b-2 F3) share one definition of "open the queue + freeze the clock" and
/// cannot drift apart.
/// </summary>
public interface IBotRecoveryMaterialiser
{
    /// <summary>
    /// Stages a <see cref="BotRecoveryItem"/> (PENDING) for <paramref name="transaction"/>
    /// and, unless it is already on hold or terminal, an auto-EMERGENCY_HOLD
    /// (freeze pre-pass → state machine → outbox notification → audit). Idempotent:
    /// returns <see cref="BotRecoveryMaterialisation.Skipped"/> when a recovery row
    /// already exists for the transaction. Does NOT call <c>SaveChanges</c> — the
    /// caller composes the staged changes into its own unit of work.
    /// </summary>
    Task<BotRecoveryMaterialisation> TryMaterialiseAsync(
        Transaction transaction,
        Guid botId,
        string botDisplayName,
        string botStatus,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IBotRecoveryMaterialiser"/>
public sealed class BotRecoveryMaterialiser : IBotRecoveryMaterialiser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly ITimeoutFreezeService _freeze;
    private readonly IOutboxService _outbox;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _clock;

    public BotRecoveryMaterialiser(
        AppDbContext db,
        ITimeoutFreezeService freeze,
        IOutboxService outbox,
        IAuditLogger audit,
        TimeProvider clock)
    {
        _db = db;
        _freeze = freeze;
        _outbox = outbox;
        _audit = audit;
        _clock = clock;
    }

    public async Task<BotRecoveryMaterialisation> TryMaterialiseAsync(
        Transaction transaction,
        Guid botId,
        string botDisplayName,
        string botStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Idempotency — a recovery row already exists for this transaction (event
        // redelivery, or the sweep and the webhook safety net both seeing the same
        // stuck item). The unique index on TransactionId is the final backstop;
        // this skips the staging work up front.
        var alreadyOpen = await _db.Set<BotRecoveryItem>()
            .AnyAsync(r => r.TransactionId == transaction.Id, cancellationToken);
        if (alreadyOpen)
        {
            return BotRecoveryMaterialisation.Skipped;
        }

        var item = new BotRecoveryItem
        {
            Id = Guid.NewGuid(),
            PlatformSteamBotId = botId,
            TransactionId = transaction.Id,
            RecoveryStatus = BotRecoveryStatus.PENDING,
            StatusAtRestriction = transaction.Status,
        };
        _db.Set<BotRecoveryItem>().Add(item);

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        var holdReason =
            $"Bot {botDisplayName} kısıtlandı ({botStatus}) — emanetteki item recovery bekliyor.";

        // Auto-hold the transaction so its timeout stops while the item is stuck.
        // Skip when already held (e.g. fraud/sanctions hold) or terminal (cancelled
        // awaiting refund — nothing to freeze). The !IsOnHold + non-empty reason
        // guards mean ApplyEmergencyHold cannot throw, so the freeze + hold compose
        // cleanly into the caller's batch commit.
        var autoHeld = false;
        if (!transaction.IsOnHold && !IsTerminalState(transaction.Status))
        {
            await _freeze.FreezeAsync(transaction, TimeoutFreezeReason.EMERGENCY_HOLD, cancellationToken);

            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            machine.ApplyEmergencyHold(SeedConstants.SystemUserId, holdReason);
            autoHeld = true;

            await _outbox.PublishAsync(
                new EmergencyHoldAppliedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    SellerId: transaction.SellerId,
                    BuyerId: transaction.BuyerId,
                    ItemName: transaction.ItemName,
                    Reason: holdReason,
                    OccurredAt: occurredAt),
                cancellationToken);

            await _audit.LogAsync(
                new AuditLogEntry(
                    UserId: null,
                    ActorId: SeedConstants.SystemUserId,
                    ActorType: ActorType.SYSTEM,
                    Action: AuditAction.EMERGENCY_HOLD_APPLIED,
                    EntityType: nameof(Transaction),
                    EntityId: transaction.Id.ToString(),
                    OldValue: null,
                    NewValue: JsonSerializer.Serialize(new
                    {
                        Reason = holdReason,
                        PreviousStatus = transaction.Status.ToString(),
                        BotRestrictionHold = true,
                    }, JsonOptions),
                    IpAddress: null),
                cancellationToken);
        }

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: SeedConstants.SystemUserId,
                ActorType: ActorType.SYSTEM,
                Action: AuditAction.BOT_RECOVERY_ITEM_CREATED,
                EntityType: nameof(BotRecoveryItem),
                EntityId: item.Id.ToString(),
                OldValue: null,
                NewValue: JsonSerializer.Serialize(new
                {
                    BotId = botId,
                    TransactionId = transaction.Id,
                    StatusAtRestriction = transaction.Status.ToString(),
                    AutoHeld = autoHeld,
                }, JsonOptions),
                IpAddress: null),
            cancellationToken);

        return new BotRecoveryMaterialisation(Created: true, AutoHeld: autoHeld);
    }

    private static bool IsTerminalState(TransactionStatus status) => status switch
    {
        TransactionStatus.COMPLETED => true,
        TransactionStatus.CANCELLED_TIMEOUT => true,
        TransactionStatus.CANCELLED_SELLER => true,
        TransactionStatus.CANCELLED_BUYER => true,
        TransactionStatus.CANCELLED_ADMIN => true,
        _ => false,
    };
}
