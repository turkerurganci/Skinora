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
        // T123 — renamed from `trade_offer_seller_timeout_minutes` /
        // `trade_offer_buyer_timeout_minutes` (07 §7.6a, 03 §2.3, 02 §3.1).
        // Both names were custodial leftovers and BOTH were misleading in v3.0:
        // no trade offer is created by the platform any more, and — the costly
        // half — the "buyer" key now feeds the SELLER's delivery window, so an
        // admin was tuning seller non-delivery through a box labelled "buyer
        // trade offer timeout" (T119 responsibility audit). The row Ids are
        // unchanged, so the migration is an UpdateData: any value an admin has
        // already configured survives the rename.
        // Env var bootstrap follows the key automatically
        // (SettingsBootstrapService: SKINORA_SETTING_{KEY_UPPER}) — the deploy
        // names are now SKINORA_SETTING_SELLER_CONFIRM_TIMEOUT_MINUTES and
        // SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES (DEPLOY_RUNBOOK §A #2/#6).
        Unconfigured( 2, "seller_confirm_timeout_minutes",              "int",     "Timeout",     "Satıcı hazırlık onayı penceresi — alıcı kabul ettikten sonra satıcının 'göndermeye hazırım' demesi için tanınan süre (03 §2.3). Dolarsa işlem satıcı kusuruyla iptal olur (02 §3.1)."),
        Unconfigured( 3, "payment_timeout_min_minutes",                 "int",     "Timeout",     "Ödeme timeout minimum"),
        Unconfigured( 4, "payment_timeout_max_minutes",                 "int",     "Timeout",     "Ödeme timeout maksimum"),
        Unconfigured( 5, "payment_timeout_default_minutes",             "int",     "Timeout",     "Ödeme timeout varsayılan"),
        Unconfigured( 6, "delivery_timeout_minutes",                    "int",     "Timeout",     "Satıcı teslimat penceresi — ödeme emanete girdikten sonra satıcının item'ı doğrudan alıcıya göndermesi için tanınan süre (02 §2.2 adım 6). Ölçülmemiş bir değerdir; launch'ta muhafazakâr YÜKSEK tutulur (DEPLOY_RUNBOOK §A #6)."),
        // WP12 (T83a/T45) — seeded with the 06 §3.17 documented default (0.75)
        // so it is no longer deploy-mandatory. Consumed two ways: the warning
        // notification job (TimeoutSchedulingService) schedules at ratio × window,
        // and the read-path timeout DTOs (07 §7.1/§7.5 WarningThresholdPercent)
        // surface ratio × 100. Admin-tunable open-(0,1) ratio (SystemSettingsValidator).
        Default     ( 7, "timeout_warning_ratio",                       "decimal", "Timeout",     "0.75",  "Uyarı gönderim oranı (ör: 0.75)"),
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
        // WP4a — seeded wide (1.0 = %100) per 08 §7.3: deviation =
        // |quoted-market|/market is a ratio that legitimately exceeds 1, and a
        // wide threshold absorbs Steam single-source variance. Admin-tunable
        // (>0, NOT an open-(0,1) ratio — SystemSettingsValidator). The
        // PRICE_DEVIATION rule fires only when both this is configured AND
        // SteamMarket:Provider=steam-market supplies a live price.
        Default     (18, "price_deviation_threshold",                   "decimal", "Fraud",       "1.0",   "Piyasa fiyat sapma eşiği (oran; 1.0 = %100). |girilen−piyasa|/piyasa bu oranı aşarsa işlem FLAGGED. 08 §7.3 tek-kaynak varyansı için geniş tutulmasını önerir; >0 olmalı (open-(0,1) ratio değil)."),
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
        // Since Prova-GasFeeChargedIsFixedGuess (2026-09-02) the charged value comes from
        // the sidecar's pre-send estimate (triggerconstantcontract + resource/price probes,
        // ChargedGasFeeResolver); this setting is only the FALLBACK when that estimate is
        // unavailable, and still feeds the 2× minimum-refund threshold in that case.
        Default     (50, "blockchain.refund_gas_fee_estimate_usdt",     "decimal", "Monitoring",    "2.0",  "İade gas fee FALLBACK değeri (USDT). Normalde kesinti sidecar'ın gönderim öncesi zincir tahmininden gelir (Prova-GasFeeChargedIsFixedGuess, 2026-09-02); tahmin alınamazsa RefundDecisionService bu değeri kullanır ve iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine bu değerle karar verilir."),
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
        // --- T77: Hot wallet monitoring job (05 §3.3) ---
        // Periodic hot wallet balance monitor (independent of the daily
        // reconciliation pass). Default 15 dakika — operationally adequate
        // without burning TronGrid quota; admin-tunable. TRX minimum is
        // 100 TRX (~50 stablecoin transfers' worth of gas headroom); below
        // that threshold a SECURITY_EVENT audit + admin SignalR alert fires.
        Default     (57, "hot_wallet.monitor_cron",                       "string",  "Monitoring",    "*/15 * * * *", "Hot wallet bakiye monitor job'unun cron ifadesi (T77 — 05 §3.3). Default '*/15 * * * *' (her 15 dakikada bir). Değiştirildikten sonra host restart gerekir (admin runtime override T96 devir)."),
        Default     (58, "hot_wallet.trx_balance_minimum",                "decimal", "Wallet",        "100",          "Hot wallet TRX bakiye alt eşiği (TRX, gas için). Bu değerin altına düşerse HOT_WALLET_THRESHOLD_BREACHED audit + admin SignalR alert fırlar (T77 — 05 §3.3). MVP ölçeğinde 100 TRX ≈ 50 TRC-20 transfer gas worst-case headroom."),
        // --- WP1: Seller-payout gas fee estimate (02 §4.7, 09 §14.4, 04 §7.3) ---
        // Separate from blockchain.refund_gas_fee_estimate_usdt because the
        // seller-send gas is the quantity the protection split measures against
        // the commission threshold (04 §7.3 worked example uses 0.50). Fed to
        // CalculateSellerPayout when SellerPayoutQueueJob enqueues the
        // SELLER_PAYOUT row. Since Prova-GasFeeChargedIsFixedGuess (2026-09-02)
        // the split input comes from the sidecar's pre-send estimate; this
        // setting is only the FALLBACK when that estimate is unavailable.
        Default     (59, "blockchain.payout_gas_fee_estimate_usdt",       "decimal", "Commission",    "0.50",         "Satıcı payout gas fee FALLBACK değeri (USDT). Normalde gas-fee koruma split'inin (02 §4.7) girdisi sidecar'ın gönderim öncesi zincir tahmininden gelir (Prova-GasFeeChargedIsFixedGuess, 2026-09-02); tahmin alınamazsa SellerPayoutQueueJob bu değeri kullanır: gasFee komisyon×%10 eşiğini aşarsa aşan kısım satıcının alacağından düşülür (04 §7.3 örneği: 0.50 → satıcıdan 0.30)."),
        // --- T125: Delivery evidence launch gate (02 §9.2, DEPLOY_RUNBOOK §H) ---
        // Seeded FALSE and deliberately not env-bootstrappable (Default(...) ⇒
        // IsConfigured = true, which SettingsBootstrapService never overrides):
        // the switch may only be flipped from the admin UI, by a person who has
        // read the captured evidence. T122 could not measure delivery latency,
        // assetid rotation or Item Certificate persistence without a real trade
        // (runbook §7), so until those rows exist the inventory inference is
        // recorded and surfaced but does not release money on its own.
        Default     (60, "delivery.inventory_evidence_auto_release_enabled", "bool", "Delivery",     "false",        "Envanter kanıtına dayalı OTOMATİK teslimat onayı açık mı (02 §9.2 launch kapısı). false iken `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` kanıtı kayda geçer ve ekranda görünür ama parayı tek başına serbest bırakmaz — insan incelemesi gerekir. Alıcının kendi 'teslim aldım' onayı bu kapıdan ETKİLENMEZ. İlk N gerçek teslimatın kanıtı (DeliveryEvidenceCaptures) incelendikten sonra true yapılır (DEPLOY_RUNBOOK §H)."),
        // --- T129: Settlement window + trade-reversal guard (02 §4.5.1, 03 §2.4) ---
        // The wait and the check are one mechanism: waiting alone protects
        // nothing, the END-OF-WINDOW re-read is what closes the reversal path.
        // The window default is 8 days = Steam's 7-day reversal window + 1 day
        // margin; the validator floors it at 7 because a shorter window would
        // let the seller reverse the trade AFTER being paid (02 §16.2).
        Default     (61, "payout_settlement_days",                        "int",  "Settlement",   "8",            "Mutabakat süresi (gün) — teslimat doğrulandıktan sonra satıcı ödemesinin bekletileceği süre (02 §4.5.1). `PayoutEligibleAt = ItemDeliveredAt + bu değer` olarak ITEM_DELIVERED girişinde hesaplanır; süre dolmadan ne satıcı payout'u ne de depozit sweep'i kuyruğa girer. Steam'in 7 günlük trade geri alma penceresini kapsamalıdır — 7'nin altına ayarlanamaz (02 §16.2)."),
        Default     (62, "settlement.unreadable_escalation_hours",        "int",  "Settlement",   "48",           "Mutabakat sonu kontrolü envanter okunamadığı için sonuca varamadığında, kaç saat sonra admin'e eskale edileceği (03 §2.4 adım 2 üçüncü dal). Eşiğe kadar kontrol her turda tekrarlanır; eşik aşılınca admin bildirimi gider ve işlem insan incelemesine düşer. Ödeme her iki durumda da parkta kalır — eşik yalnızca 'ne zaman insana sorulur' sorusunu yanıtlar, ödemeyi serbest bırakmaz."),
        Default     (63, "settlement.reversal_auto_refund_enabled",       "bool", "Settlement",   "false",        "Geri alma tespitinde OTOMATİK iade açık mı (T129 launch kapısı, T125 kapısının ikizi). false iken imza kayda geçer ve admin'e eskale edilir, para hareket etmez; kararı admin verir — satıcı lehine AD32 clear-settlement, alıcı lehine dispute üzerinden AD29. İki kol AYNI sonucu üretmez: DeliveryReversedAt'i yalnız otomatik dal yazar, itibar paydası ve fraud flag yalnız orada oluşur. true iken imza delivery_reversed tetikler. Gerçek geri alma ölçülene kadar (T122 §7) kapalı kalır."),
        // --- Gas fee tavani (2026-09-04, gas fee turu) ---
        // Runtime tahmin bir zincir probunu CANLI kurla carpiyor; tek bir bozuk
        // kotasyon, birim kaymasi ya da yanlis okunan ondalik, kullanicinin KENDI
        // parasindan sinirsiz bir kesintiye donusebilirdi ve asagi akista bunu
        // sorgulayan hicbir kapi yoktu. Tahmin bu tavani asarsa KIRPILMAZ —
        // kirpilmis bir rakam yanlis ama makul gorunur; tahmin reddedilir,
        // statik fallback kesilir ve hata loglanir (GasFeeSource.EstimateRejected).
        Default     (64, "blockchain.max_charged_gas_fee_usdt",           "decimal", "Monitoring",   "10.0",         "Kullanicidan kesilebilecek gas fee ust siniri (USDT). Runtime tahmin bu degeri asarsa tahmin REDDEDILIR (kirpilmaz) ve statik fallback kesilir; admin logu duser. Gercek bir mainnet TRC-20 gonderimi ~6,4 TRX (~2 USDT) yaktigi icin varsayilan 10.0 saglikli hicbir tahmini tetiklemez — bozuk bir tahmini yakalamak icindir. 0 = tavan kapali."),
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
