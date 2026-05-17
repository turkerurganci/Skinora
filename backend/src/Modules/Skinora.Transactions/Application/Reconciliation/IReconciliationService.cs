namespace Skinora.Transactions.Application.Reconciliation;

/// <summary>
/// Compares on-chain balances against the platform ledger across three
/// scopes (T76 — 05 §3.3): each active deposit address, the hot wallet, and
/// the cold wallet. Any (scope, token) discrepancy is recorded as a
/// <c>RECONCILIATION_MISMATCH</c> <see cref="Platform.Domain.Entities.AuditLog"/>
/// row and broadcast to admin clients over SignalR. Designed to be invoked
/// once per day by the recurring
/// <see cref="ReconciliationJob"/>; sidecar / TronGrid failures are logged
/// rather than retried so the job is idempotent under concurrent runs.
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// Runs a single end-to-end reconciliation sweep. Returns a structured
    /// outcome so the Hangfire wrapper can log a summary without re-reading
    /// the audit table.
    /// </summary>
    Task<ReconciliationOutcome> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Summary returned by <see cref="IReconciliationService.RunAsync"/>.
/// </summary>
public sealed record ReconciliationOutcome(
    int DepositAddressesChecked,
    bool HotWalletChecked,
    bool ColdWalletChecked,
    int MismatchCount,
    long? BlockNumber);

/// <summary>
/// Reconciliation scope tag (T76 — 05 §3.3). Used as the AuditLog
/// <c>EntityType</c> column and as the realtime push <c>Scope</c> field so
/// admin clients can filter by area without parsing the JSON payload.
/// </summary>
public enum ReconciliationScope
{
    DepositAddress,
    HotWallet,
    ColdWallet,
}
