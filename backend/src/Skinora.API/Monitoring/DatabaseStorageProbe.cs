using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;

namespace Skinora.API.Monitoring;

/// <summary>
/// T164 — one reading of the application database: data/log file sizes, the
/// server edition, the recovery model and the age of the newest scheduled
/// backups. Ages are computed on the server (<c>GETDATE()</c> against
/// <c>backup_finish_date</c>, both server-local), so the server's time zone
/// never skews them. <see langword="null"/> age: no such backup in
/// <c>msdb</c> (or the history could not be read).
/// </summary>
public sealed record DatabaseStorageSnapshot(
    int EngineEdition,
    string RecoveryModel,
    long DataAllocatedBytes,
    long DataUsedBytes,
    long LogAllocatedBytes,
    long? FullBackupAgeSeconds,
    long? LogBackupAgeSeconds,
    bool BackupHistoryRead);

public interface IDatabaseStorageProbe
{
    /// <summary>The current reading, or <see langword="null"/> when the database is not SQL Server.</summary>
    Task<DatabaseStorageSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads <see cref="DatabaseStorageSnapshot"/> through the application's own
/// connection (no exporter, no extra login — DEPLOY_RUNBOOK §M.5). The size
/// comes from <c>sys.database_files</c> of the current database; the backup
/// history from <c>msdb.dbo.backupset</c>, which needs read access to
/// <c>msdb</c> (the deployment's <c>sa</c> login has it). A failed history read
/// does not lose the size reading: the history is a separate command.
/// </summary>
public sealed class SqlServerDatabaseStorageProbe : IDatabaseStorageProbe
{
    // type 0 = ROWS (data), 1 = LOG; size and SpaceUsed are 8 KB pages.
    internal const string StorageSql = """
        SET NOCOUNT ON;
        SELECT
            CAST(SERVERPROPERTY('EngineEdition') AS int),
            (SELECT recovery_model_desc FROM sys.databases WHERE database_id = DB_ID()),
            (SELECT COALESCE(SUM(CAST(size AS bigint)), 0) * 8192 FROM sys.database_files WHERE type = 0),
            (SELECT COALESCE(SUM(CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint)), 0) * 8192 FROM sys.database_files WHERE type = 0),
            (SELECT COALESCE(SUM(CAST(size AS bigint)), 0) * 8192 FROM sys.database_files WHERE type = 1);
        """;

    // Copy-only backups (an operator's ad-hoc copy) are not the schedule.
    internal const string BackupHistorySql = """
        SET NOCOUNT ON;
        SELECT
            (SELECT DATEDIFF_BIG(SECOND, MAX(backup_finish_date), GETDATE()) FROM msdb.dbo.backupset
              WHERE database_name = DB_NAME() AND type = 'D' AND is_copy_only = 0),
            (SELECT DATEDIFF_BIG(SECOND, MAX(backup_finish_date), GETDATE()) FROM msdb.dbo.backupset
              WHERE database_name = DB_NAME() AND type = 'L' AND is_copy_only = 0);
        """;

    private readonly AppDbContext _db;
    private readonly ILogger<SqlServerDatabaseStorageProbe> _logger;

    public SqlServerDatabaseStorageProbe(AppDbContext db, ILogger<SqlServerDatabaseStorageProbe> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DatabaseStorageSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsSqlServer()) return null;

        var connection = _db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            int edition;
            string recovery;
            long dataAllocated, dataUsed, logAllocated;
            await using (var command = Command(connection, StorageSql))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("The storage query returned no row.");
                edition = reader.GetInt32(0);
                recovery = reader.IsDBNull(1) ? "UNKNOWN" : reader.GetString(1);
                dataAllocated = reader.GetInt64(2);
                dataUsed = reader.GetInt64(3);
                logAllocated = reader.GetInt64(4);
            }

            long? fullAge = null, logAge = null;
            var historyRead = false;
            try
            {
                await using var command = Command(connection, BackupHistorySql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    fullAge = reader.IsDBNull(0) ? null : reader.GetInt64(0);
                    logAge = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                    historyRead = true;
                }
            }
            catch (DbException ex)
            {
                _logger.LogWarning(ex,
                    "Database backup history (msdb.dbo.backupset) could not be read — the backup age series keep their last value (DEPLOY_RUNBOOK §M.5).");
            }

            return new DatabaseStorageSnapshot(
                edition, recovery, dataAllocated, dataUsed, logAllocated, fullAge, logAge, historyRead);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    private static DbCommand Command(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return command;
    }
}
