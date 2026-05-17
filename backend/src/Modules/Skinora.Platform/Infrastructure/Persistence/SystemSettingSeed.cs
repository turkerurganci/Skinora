using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Domain.Seed;

namespace Skinora.Platform.Infrastructure.Persistence;

/// <summary>
/// Deterministic seed contract for <see cref="SystemSetting"/> (06 §8.9, §3.17).
/// </summary>
/// <remarks>
/// <para>
/// Parameters with a documented default (e.g. <c>commission_rate = 0.02</c>)
/// are seeded as <c>IsConfigured = true</c>. Parameters whose default is "—"
/// ship as <c>Value = NULL, IsConfigured = false</c> and must be hydrated
/// by admin or the <c>SKINORA_SETTING_{KEY_UPPER}</c> env var bootstrap
/// before the API completes startup (06 §8.9 fail-fast).
/// </para>
/// <para>
/// Key order matches 06 §3.17 row order. Guids are derived from the namespace
/// prefix plus the row index so rerunning <c>EnsureCreated</c> / regenerating
/// migrations always produces the same values.
/// </para>
/// </remarks>
public static class SystemSettingSeed
{
    private const string GuidNamespacePrefix = "0aa51010-0000-0000-0000-00000000";

    private static Guid IdFor(int index) =>
        new($"{GuidNamespacePrefix}{index:x4}");

    public static IReadOnlyList<SystemSetting> All { get; } =
    [
        Unconfigured( 1, "accept_timeout_minutes",                      "int",     "Timeout",     "Alıcı kabul timeout süresi"),
        Unconfigured( 2, "trade_offer_seller_timeout_minutes",          "int",     "Timeout",     "Satıcı trade offer timeout süresi"),
        Unconfigured( 3, "payment_timeout_min_minutes",                 "int",     "Timeout",     "Ödeme timeout minimum"),
        Unconfigured( 4, "payment_timeout_max_minutes",                 "int",     "Timeout",     "Ödeme timeout maksimum"),
        Unconfigured( 5, "payment_timeout_default_minutes",             "int",     "Timeout",     "Ödeme timeout varsayılan"),
        Unconfigured( 6, "trade_offer_buyer_timeout_minutes",           "int",     "Timeout",     "Alıcı trade offer timeout süresi"),
        Unconfigured( 7, "timeout_warning_ratio",                       "decimal", "Timeout",     "Uyarı gönderim oranı (ör: 0.75)"),
        Default     ( 8, "commission_rate",                             "decimal", "Commission",  "0.02",  "Komisyon oranı (%2)"),
        Unconfigured( 9, "min_transaction_amount",                      "decimal", "Limit",       "Minimum işlem tutarı"),
        Unconfigured(10, "max_transaction_amount",                      "decimal", "Limit",       "Maksimum işlem tutarı"),
        Unconfigured(11, "max_concurrent_transactions",                 "int",     "Limit",       "Eşzamanlı aktif işlem limiti"),
        Unconfigured(12, "new_account_transaction_limit",               "int",     "Limit",       "Yeni hesap işlem limiti"),
        Unconfigured(13, "new_account_period_days",                     "int",     "Limit",       "Kaç gün yeni hesap sayılır"),
        Unconfigured(14, "cancel_limit_count",                          "int",     "Limit",       "Belirli sürede izin verilen iptal sayısı"),
        Unconfigured(15, "cancel_limit_period_hours",                   "int",     "Limit",       "İptal limit periyodu"),
        Unconfigured(16, "cancel_cooldown_hours",                       "int",     "Limit",       "İptal sonrası cooldown süresi"),
        Default     (17, "gas_fee_protection_ratio",                    "decimal", "Commission",  "0.10",  "Gas fee koruma eşiği (%10)"),
        Unconfigured(18, "price_deviation_threshold",                   "decimal", "Fraud",       "Piyasa fiyat sapma eşiği"),
        Unconfigured(19, "high_volume_amount_threshold",                "decimal", "Fraud",       "Yüksek hacim tutar eşiği"),
        Unconfigured(20, "high_volume_count_threshold",                 "int",     "Fraud",       "Yüksek hacim işlem sayısı eşiği"),
        Unconfigured(21, "high_volume_period_hours",                    "int",     "Fraud",       "Yüksek hacim kontrol periyodu"),
        Default     (22, "monitoring_post_cancel_24h_polling_seconds",  "int",     "Monitoring",  "30",    "İptal sonrası ilk 24 saat polling aralığı (saniye)"),
        Default     (23, "monitoring_post_cancel_7d_polling_seconds",   "int",     "Monitoring",  "300",   "1-7 gün arası polling aralığı (saniye)"),
        Default     (24, "monitoring_post_cancel_30d_polling_seconds",  "int",     "Monitoring",  "3600",  "7-30 gün arası polling aralığı (saniye)"),
        Default     (25, "monitoring_stop_after_days",                  "int",     "Monitoring",  "30",    "İzleme durdurma süresi (gün)"),
        Default     (26, "min_refund_threshold_ratio",                  "decimal", "Monitoring",  "2.0",   "Minimum iade eşiği — iade < gas fee × bu oran ise iade yapılmaz"),
        Default     (27, "open_link_enabled",                           "bool",    "Feature",     "false", "Açık link yöntemi aktif mi"),
        Unconfigured(28, "hot_wallet_limit",                            "decimal", "Wallet",      "Hot wallet maksimum bakiye limiti — aşıldığında admin alert (05 §3.3)"),
        // --- T30: Access control settings (02 §21.1, 03 §11a.1, §11a.2) ---
        Default     (29, "auth.banned_countries",                       "string",  "AccessControl", "NONE", "Geo-block — ISO-3166-1 alpha-2 ülke kodları CSV (örn: 'IR,KP,CU'); 'NONE' hiçbir ülke engellenmemiş demektir. Admin tarafından yönetilir."),
        Default     (30, "auth.min_steam_account_age_days",             "int",     "AccessControl", "30",   "Steam hesap minimum yaş eşiği (gün) — burner/fake hesap caydırıcı. Hesap yaşı bu değerden az ise giriş engellenir (02 §21.1, 03 §11a.2)."),
        // --- T34: Wallet address change cooldown (02 §12.3, 03 §9.2) ---
        Default     (31, "wallet.payout_address_cooldown_hours",        "int",     "Wallet",        "24",   "Satıcı ödeme adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma engellenir; mevcut CREATED davetler eski snapshot adresle devam eder (02 §12.3)."),
        Default     (32, "wallet.refund_address_cooldown_hours",        "int",     "Wallet",        "24",   "Alıcı iade adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma ve işlem kabul etme engellenir (02 §12.3)."),
        // --- T43: Reputation insufficient-data thresholds (02 §13, 06 §3.1) ---
        Default     (33, "reputation.min_account_age_days",             "int",     "Reputation",    "30",   "Yeni hesap koruması — hesap yaşı bu eşiğin altındaysa composite reputationScore null döner ('Yeni kullanıcı')."),
        Default     (34, "reputation.min_completed_transactions",       "int",     "Reputation",    "3",    "İstatistiksel anlamlılık — tamamlanmış işlem sayısı bu eşiğin altındaysa composite reputationScore null döner."),
        // --- T55: Dormant-account anomaly thresholds (02 §14.3, §14.4) ---
        Default     (35, "dormant_account_min_age_days",                "int",     "Fraud",         "30",   "Dormant kontrolü için minimum hesap yaşı (gün). Bu yaşın altında hesap 'yeni hesap' sayılır ve T39 yeni hesap limitleri uygulanır; bu eşiğin üzerinde 0 işlemli hesabın yüksek tutarlı denemesi ABNORMAL_BEHAVIOR ile flag'lenir (02 §14.3)."),
        Unconfigured(36, "dormant_account_value_threshold",             "decimal", "Fraud",                  "Dormant hesap için tek işlem tutar eşiği (USDT). Hiç işlem yapmamış hesabın bu tutarın üzerinde işlem denemesi otomatik flag tetikler. Admin tarafından risk profiline göre belirlenir."),
        // --- T56: Multi-account detection — known exchange/custodial address allowlist (02 §14.3, 03 §7.4) ---
        Default     (37, "multi_account.exchange_addresses",            "string",  "Fraud",         "NONE", "Çoklu hesap kontrolünde 'aynı gönderim adresi' destekleyici sinyalinden hariç tutulan bilinen exchange/custodial cüzdan adresleri (CSV). 'NONE' = hiç adres hariç değil. Adresler exact-match (case-sensitive) karşılaştırılır."),
        // --- T63a: Platform maintenance state (07 §10.2, 03 §11.1–§11.3) ---
        // String columns store the literal "NONE" as the inactive sentinel — the
        // /platform/maintenance contract emits it as JSON null. Cross-key invariant
        // (active=true ⇒ type≠NONE) is enforced by SystemSettingsValidator.
        Default     (38, "platform.maintenance.active",                 "bool",    "Platform",      "false","Platform/Steam/blockchain bakım veya kesinti aktif mi (07 §10.2). true iken type set edilmiş olmalı."),
        Default     (39, "platform.maintenance.type",                   "string",  "Platform",      "NONE", "Bakım/kesinti tipi: PLANNED_MAINTENANCE | PLATFORM_MAINTENANCE | STEAM_OUTAGE | BLOCKCHAIN_DEGRADATION | NONE (NONE = aktif değil, 07 §10.2)."),
        Default     (40, "platform.maintenance.message",                "string",  "Platform",      "NONE", "Kullanıcıya gösterilecek bilgilendirme mesajı. 'NONE' = mesaj yok (07 §10.2 maintenance banner)."),
        Default     (41, "platform.maintenance.planned_end",            "string",  "Platform",      "NONE", "Tahmini bitiş zamanı (ISO 8601 UTC, ör: '2026-03-16T18:00:00Z'). 'NONE' = bilinmiyor / aktif değil (07 §10.2)."),
        // --- T63b: Retention job ages + batch sizes (06 §1, §3.18, §3.19, §3.21, §6.1) ---
        // Age keys store the retention window in days; the cleanup jobs read them
        // at start of each run and fall back to a code constant if unconfigured.
        // Batch sizes cap per-iteration DELETE volume so a single sweep cannot
        // monopolise the connection or saturate the log file.
        Default     (42, "retention.outbox_message_days",               "int",     "Retention",     "30",   "Processed OutboxMessage retention süresi (gün, 06 §3.18). Status=PROCESSED ve ProcessedAt bu süreden eski kayıtlar OutboxRetentionCleanupJob tarafından toplu hard delete edilir."),
        Default     (43, "retention.processed_event_days",              "int",     "Retention",     "30",   "ProcessedEvent retention süresi (gün, 06 §3.19). ProcessedAt bu süreden eski kayıtlar — OutboxMessage temizlenmeden önce — toplu hard delete edilir. FK olmadığı için silme sırası uygulama seviyesinde garanti edilir."),
        Default     (44, "retention.external_idempotency_days",         "int",     "Retention",     "30",   "ExternalIdempotencyRecord retention süresi (gün, 06 §3.21). Status=completed ve CompletedAt bu süreden eski kayıtlar toplu hard delete edilir. in_progress ve failed kayıtlar lease/retry akışına bırakılır."),
        Default     (45, "retention.orphan_notification_days",          "int",     "Retention",     "365",  "Bağımsız bildirim (Notification, TransactionId IS NULL) retention süresi (gün, 06 §1, §6.1). CreatedAt bu süreden eski kayıtlar bağlı NotificationDelivery kayıtlarıyla birlikte (önce delivery, sonra notification) toplu hard delete edilir."),
        Default     (46, "retention.user_login_log_days",               "int",     "Retention",     "365",  "UserLoginLog retention süresi (gün, 06 §1, §6.1). CreatedAt bu süreden eski kayıtlar toplu hard delete edilir (soft-delete kontrolü dışındadır — retention IsDeleted flag'inden bağımsız çalışır)."),
        Default     (47, "retention.batch_size_outbox",                 "int",     "Retention",     "1000", "Outbox retention job'unun tek SELECT+DELETE iterasyonunda işleyebileceği maksimum kayıt sayısı. Job, eligible kayıt kalmayana kadar batch'leri tekrarlar."),
        Default     (48, "retention.batch_size_notification",           "int",     "Retention",     "500",  "Bağımsız bildirim retention job'unun tek iterasyonda işleyebileceği maksimum Notification sayısı. Bağlı NotificationDelivery kayıtları aynı iterasyon içinde silinir."),
        Default     (49, "retention.batch_size_user_login_log",         "int",     "Retention",     "1000", "UserLoginLog retention job'unun tek iterasyonda işleyebileceği maksimum kayıt sayısı."),
        // --- T72: Blockchain amount validation — refund gas fee estimate (08 §3.4, 02 §4.4, 09 §14.4) ---
        // T74 will replace this MVP estimate with a live TronGrid-derived value; for now
        // the refund-decision path uses this admin-tunable USDT amount when classifying
        // under/over/wrong-token cases against the 2× gas fee minimum threshold.
        Default     (50, "blockchain.refund_gas_fee_estimate_usdt",     "decimal", "Monitoring",    "2.0",  "T72 MVP iade gas fee tahmini (USDT). RefundDecisionService bu değeri kullanarak iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine karar verir. T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir."),
        // --- T73: Outbound transfer dispatcher retry intervals (08 §3.3, 05 §3.3, 11 T73) ---
        // CSV (dakika) — sıralı; her transient failure'da `RetryCount` artırılır ve bu listenin
        // RetryCount'inci elemanı `NextAttemptAt`'a eklenir. Liste tükendiğinde transfer FAILED
        // edilir ve TransferDispatchFailedEvent yayınlanır. Default "1,5,15" = 3 deneme
        // exponential backoff (1dk, 5dk, 15dk). Admin tarafından değiştirilebilir.
        Default     (51, "blockchain.transfer_retry_intervals_minutes",  "string",  "Monitoring",    "1,5,15", "Outbound transfer (payout/refund/sweep) retry aralıkları (dakika, CSV). Her transient failure NextAttemptAt'i listedeki sıradaki değerle ileriye iter; liste bittiğinde transfer FAILED + admin alert. Default '1,5,15' = T73 plan'ı."),
        // --- T74: Sweep / refund Energy delegation amounts (08 §3.3, 11 T74) ---
        // Both stored as SUN strings (1 TRX = 1_000_000 SUN). Sidecar reads matching env vars
        // (SWEEP_ENERGY_DELEGATION_SUN, SWEEP_TRX_FALLBACK_SUN) at startup; these SystemSetting
        // rows give admin visibility and a single canonical source — runtime propagation from
        // backend to sidecar is a T-future task (see T74 K1).
        Default     (52, "blockchain.sweep_energy_delegation_sun",       "string",  "Monitoring",    "200000000", "Sweep / deposit-sourced refund öncesi sweeper hot wallet'tan deposit adresine geçici Energy delegation tutarı (SUN, 1 TRX = 1_000_000 SUN). Default 200 TRX — Stake 2.0 ile ~16.000 Energy headroom (TRC-20 transfer ~65k Energy, dış API oran dalgalanması payı dahil). 08 §3.3."),
        Default     (53, "blockchain.sweep_trx_fallback_sun",            "string",  "Monitoring",    "15000000",  "Energy delegation başarısız olursa deposit adresine fallback olarak gönderilen TRX tutarı (SUN). Default 15 TRX (08 §3.3 — TRC-20 transferin gas için yaklaşık üst sınırı). Deposit bu TRX'i kendi gas'ı için yakar."),
        // --- T76: Blockchain reconciliation job (05 §3.3) ---
        // Daily on-chain vs ledger reconciliation. Cron default is 03:00 UTC
        // (admin-tunable, host restart required to re-register). Hot/cold
        // wallet addresses ship unconfigured — production deploy sets them
        // via SystemSetting before the first run, otherwise that scope is
        // skipped with a warn log.
        Default     (54, "reconciliation.schedule_cron",                 "string",  "Monitoring",    "0 3 * * *", "Reconciliation job cron ifadesi (05 §3.3). Default '0 3 * * *' (03:00 UTC günlük). Değiştirildikten sonra host restart gerekir (admin runtime override T96 devir)."),
        Default     (55, "reconciliation.hot_wallet_address",            "string",  "Monitoring",    "NONE",      "Reconciliation karşılaştırması için hot wallet Tron adresi. 'NONE' ise hot wallet kapsamı atlanır (warn log). Production deploy bu değeri ayarlamalıdır (05 §3.3) — auth.banned_countries NONE sentinel pattern."),
        Default     (56, "reconciliation.cold_wallet_address",           "string",  "Monitoring",    "NONE",      "Reconciliation karşılaştırması için cold wallet Tron adresi (opsiyonel). 'NONE' ise cold wallet kapsamı atlanır (info log). MVP'de cold transfer manuel başlatılır — ColdWalletTransfer ledger'a eşleştirilir."),
    ];

    private static SystemSetting Unconfigured(
        int index,
        string key,
        string dataType,
        string category,
        string description) => new()
        {
            Id = IdFor(index),
            Key = key,
            Value = null,
            IsConfigured = false,
            DataType = dataType,
            Category = category,
            Description = description,
            CreatedAt = SeedConstants.SeedAnchorUtc,
            UpdatedAt = SeedConstants.SeedAnchorUtc,
            RowVersion = SeedConstants.SeedRowVersion,
        };

    private static SystemSetting Default(
        int index,
        string key,
        string dataType,
        string category,
        string value,
        string description) => new()
        {
            Id = IdFor(index),
            Key = key,
            Value = value,
            IsConfigured = true,
            DataType = dataType,
            Category = category,
            Description = description,
            CreatedAt = SeedConstants.SeedAnchorUtc,
            UpdatedAt = SeedConstants.SeedAnchorUtc,
            RowVersion = SeedConstants.SeedRowVersion,
        };
}
