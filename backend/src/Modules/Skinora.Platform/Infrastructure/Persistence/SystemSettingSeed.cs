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
