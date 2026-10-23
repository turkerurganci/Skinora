using Skinora.Shared.Enums;

namespace Skinora.Transactions.Domain.Entities;

/// <summary>
/// T169a — one row per network, the lock the deposit refund daily limit's
/// check-and-stamp takes (<c>DepositRefundDailyLimit.LockAsync</c>, 08 §3.3c).
/// The dispatcher updates its network's row inside the check's transaction;
/// the update's exclusive row lock, held until the transaction ends, makes a
/// second dispatcher of the same network wait, so two refunds can never both
/// read the same 24-hour total. A table of its own (no <c>RowVersion</c>, no
/// audit) so taking the lock never touches a row anyone else edits.
/// </summary>
public class DepositRefundLimitLock
{
    public BlockchainNetwork Network { get; set; }

    /// <summary>When the lock was last taken (diagnostics only).</summary>
    public DateTime? LockedAt { get; set; }
}
