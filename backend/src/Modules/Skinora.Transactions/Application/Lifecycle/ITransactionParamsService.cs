namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Reads admin-configured form parameters for the transaction creation form
/// (T45 — 07 §7.4). Pure read path; no caching layer (admin updates are
/// expected to take effect immediately, mirroring the T30/T34/T43 settings
/// reader pattern).
/// </summary>
public interface ITransactionParamsService
{
    Task<TransactionParamsDto> GetAsync(CancellationToken cancellationToken);
}
