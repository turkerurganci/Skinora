namespace Skinora.Steam.Application.Admin;

/// <summary>
/// S18 Recovery Queue read + triage service (T103b-2 — 02 §15, 03 §11.2a,
/// 04 §8.7). Backs AD25 (list a bot's recovery queue) and AD26 (update a
/// recovery item's triage state). Materialisation of recovery items is owned by
/// <c>BotRestrictionRecoveryConsumer</c>, not this service.
/// </summary>
public interface IAdminBotRecoveryService
{
    /// <summary>
    /// AD25 — the recovery queue for one bot. Returns null when the bot does not
    /// exist (or is soft-deleted) so the controller can answer 404.
    /// </summary>
    Task<BotRecoveryQueueResponse?> GetQueueAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>
    /// AD26 — apply an admin triage update (note / responsible admin / status) to
    /// a recovery item and write a <c>BOT_RECOVERY_UPDATED</c> audit row.
    /// </summary>
    Task<UpdateRecoveryItemOutcome> UpdateAsync(
        Guid adminUserId,
        Guid recoveryItemId,
        UpdateRecoveryItemRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);
}
