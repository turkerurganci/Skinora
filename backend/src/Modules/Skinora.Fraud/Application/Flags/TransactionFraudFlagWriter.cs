using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Fraud.Application.Flags;

/// <summary>
/// Adapter wiring the Skinora.Transactions
/// <see cref="ITransactionFraudFlagWriter"/> port to
/// <see cref="IFraudFlagService.StageTransactionFlagAsync"/>. Centralised so
/// a future signal generator that wants to emit pre-create flags from inside
/// <c>Skinora.Transactions</c> still routes through the audit / outbox
/// pipeline owned by the Fraud module (T54).
/// </summary>
public sealed class TransactionFraudFlagWriter : ITransactionFraudFlagWriter
{
    private readonly IFraudFlagService _flagService;

    public TransactionFraudFlagWriter(IFraudFlagService flagService)
    {
        _flagService = flagService;
    }

    public Task StagePreCreateFlagAsync(
        Guid userId,
        Guid transactionId,
        FraudFlagType type,
        string details,
        CancellationToken cancellationToken)
        => _flagService.StageTransactionFlagAsync(
            userId,
            transactionId,
            type,
            details,
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cancellationToken);
}
