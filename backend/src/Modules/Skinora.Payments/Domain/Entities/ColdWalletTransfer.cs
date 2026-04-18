using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Payments.Domain.Entities;

/// <summary>
/// Hot → cold wallet ledger entry. All fields per 06 §3.22.
/// </summary>
/// <remarks>
/// Append-only: INSERT only — UPDATE and DELETE are rejected at the
/// <c>AppDbContext</c> level so reconciliation (05 §3.3) always has a
/// faithful ledger against which to cross-check on-chain transfers.
/// </remarks>
public class ColdWalletTransfer : IAppendOnly
{
    public long Id { get; set; }
    public decimal Amount { get; set; }
    public StablecoinType Token { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string TxHash { get; set; } = string.Empty;
    public Guid InitiatedByAdminId { get; set; }
    public DateTime CreatedAt { get; set; }
}
