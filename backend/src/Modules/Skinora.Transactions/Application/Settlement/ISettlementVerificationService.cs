using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// T129 — the 02 §4.5.1 end-of-window check: is the item still with the buyer,
/// and if it is not, did it go back to the seller?
/// </summary>
/// <remarks>
/// Pure decision surface, exactly like <c>IDeliveryVerificationService</c>: it
/// reads inventories and returns a verdict. It never writes a column, fires a
/// trigger or moves money — those belong to the caller that owns the unit of
/// work (<c>SettlementVerificationJob</c>).
/// </remarks>
public interface ISettlementVerificationService
{
    Task<SettlementVerificationResult> VerifyAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}
