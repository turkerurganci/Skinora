namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Per-key presentation metadata for the 07 §9.8 <c>GET /admin/settings</c>
/// response. Augments the storage-side <see cref="Skinora.Platform.Domain.Entities.SystemSetting"/>
/// (which only carries Key/Value/DataType/Category/Description) with the
/// label, unit and lowercase API category dialect documented by the contract.
/// </summary>
/// <remarks>
/// <para>
/// The DB <c>Category</c> column is intentionally coarser than the API
/// category set (e.g. several "Limit" rows split into <c>transaction_limits</c>,
/// <c>cancel_rules</c> and <c>new_account</c> in 07 §9.8). The catalog is the
/// single source of truth for response shaping; the DB column stays as a
/// simple index target (see <c>IX_SystemSettings_Category</c>).
/// </para>
/// <para>
/// Keys not present here are omitted from the GET response — every seed key
/// must have an entry. <see cref="SystemSettingsCatalogTests"/> enforces 1:1
/// coverage against <see cref="Skinora.Platform.Infrastructure.Persistence.SystemSettingSeed"/>.
/// </para>
/// </remarks>
public static class SystemSettingsCatalog
{
    /// <summary>API valueType per 07 §9.8 — numeric for int/decimal.</summary>
    public const string ValueTypeNumber = "number";
    public const string ValueTypeBoolean = "boolean";
    public const string ValueTypeString = "string";

    private static readonly Dictionary<string, SystemSettingMetadata> _byKey =
        BuildCatalog().ToDictionary(m => m.Key, StringComparer.Ordinal);

    /// <summary>All catalog entries, in 06 §3.17 row order (mirrors seed order).</summary>
    public static IReadOnlyList<SystemSettingMetadata> All { get; } = BuildCatalog();

    public static SystemSettingMetadata? TryGet(string key) =>
        _byKey.TryGetValue(key, out var entry) ? entry : null;

    public static bool Contains(string key) => _byKey.ContainsKey(key);

    /// <summary>Map storage <c>DataType</c> ('int','decimal','bool','string') to 07 §9.8 valueType.</summary>
    public static string ValueTypeFor(string dataType) => dataType switch
    {
        "int" or "decimal" => ValueTypeNumber,
        "bool" => ValueTypeBoolean,
        "string" => ValueTypeString,
        _ => ValueTypeString,
    };

    private static List<SystemSettingMetadata> BuildCatalog() =>
    [
        // --- Timeout (06 §3.17 / 02 §16.2 timeout süreleri) ---
        new("accept_timeout_minutes",                     "timeout",              "Alıcı kabul timeout süresi",                                "dakika"),
        new("trade_offer_seller_timeout_minutes",         "timeout",              "Satıcı trade offer timeout süresi",                         "dakika"),
        new("payment_timeout_min_minutes",                "timeout",              "Ödeme timeout minimum",                                     "dakika"),
        new("payment_timeout_max_minutes",                "timeout",              "Ödeme timeout maksimum",                                    "dakika"),
        new("payment_timeout_default_minutes",            "timeout",              "Ödeme timeout varsayılan",                                  "dakika"),
        new("trade_offer_buyer_timeout_minutes",          "timeout",              "Alıcı trade offer timeout süresi",                          "dakika"),
        new("timeout_warning_ratio",                      "timeout",              "Timeout uyarı gönderim oranı",                              "oran"),

        // --- Commission ---
        new("commission_rate",                            "commission",           "Komisyon oranı",                                            "oran"),
        new("gas_fee_protection_ratio",                   "gas_fee",              "Gas fee koruma eşiği",                                      "oran"),

        // --- Transaction limits + new account + cancel rules ---
        new("min_transaction_amount",                     "transaction_limits",   "Minimum işlem tutarı",                                      "USDT"),
        new("max_transaction_amount",                     "transaction_limits",   "Maksimum işlem tutarı",                                     "USDT"),
        new("max_concurrent_transactions",                "transaction_limits",   "Eşzamanlı aktif işlem limiti",                              "adet"),
        new("new_account_transaction_limit",              "new_account",          "Yeni hesap işlem limiti",                                   "adet"),
        new("new_account_period_days",                    "new_account",          "Yeni hesap kabul edilme süresi",                            "gün"),
        new("cancel_limit_count",                         "cancel_rules",         "İptal limiti — periyot içinde izin verilen iptal sayısı",   "adet"),
        new("cancel_limit_period_hours",                  "cancel_rules",         "İptal limit periyodu",                                      "saat"),
        new("cancel_cooldown_hours",                      "cancel_rules",         "İptal sonrası cooldown süresi",                             "saat"),

        // --- Fraud detection (price + high-volume + dormant anomaly T55) ---
        new("price_deviation_threshold",                  "fraud_detection",      "Piyasa fiyatı sapma eşiği",                                 "oran"),
        new("high_volume_amount_threshold",               "fraud_detection",      "Yüksek hacim tutar eşiği",                                  "USDT"),
        new("high_volume_count_threshold",                "fraud_detection",      "Yüksek hacim işlem sayısı eşiği",                           "adet"),
        new("high_volume_period_hours",                   "fraud_detection",      "Yüksek hacim kontrol periyodu",                             "saat"),
        new("dormant_account_min_age_days",               "fraud_detection",      "Dormant hesap minimum yaş eşiği",                           "gün"),
        new("dormant_account_value_threshold",            "fraud_detection",      "Dormant hesap işlem tutar eşiği",                           "USDT"),
        new("multi_account.exchange_addresses",           "fraud_detection",      "Bilinen exchange/custodial adres listesi (CSV; NONE = yok)", null),

        // --- Blockchain monitoring + refund threshold ---
        new("monitoring_post_cancel_24h_polling_seconds", "blockchain_health",    "İptal sonrası ilk 24 saat polling aralığı",                 "saniye"),
        new("monitoring_post_cancel_7d_polling_seconds",  "blockchain_health",    "İptal sonrası 1-7 gün polling aralığı",                     "saniye"),
        new("monitoring_post_cancel_30d_polling_seconds", "blockchain_health",    "İptal sonrası 7-30 gün polling aralığı",                    "saniye"),
        new("monitoring_stop_after_days",                 "blockchain_health",    "İzleme durdurma süresi",                                    "gün"),
        new("min_refund_threshold_ratio",                 "blockchain_health",    "Minimum iade eşiği — gas fee × bu oran altı iade yapılmaz", "oran"),

        // --- Buyer identification (open link toggle, 02 §16.2 "Yöntem 2'yi aktif/pasif") ---
        new("open_link_enabled",                          "buyer_identification", "Açık link yöntemi aktif",                                   null),

        // --- Wallet security ---
        new("hot_wallet_limit",                           "wallet_security",      "Hot wallet maksimum bakiye limiti",                         "USDT"),
        new("wallet.payout_address_cooldown_hours",       "wallet_security",      "Satıcı ödeme adresi değişikliği cooldown",                  "saat"),
        new("wallet.refund_address_cooldown_hours",       "wallet_security",      "Alıcı iade adresi değişikliği cooldown",                    "saat"),

        // --- Access control (T30 settings — geo + age gate) ---
        new("auth.banned_countries",                      "geo_blocking",         "Yasaklı ülkeler (ISO-3166-1 alpha-2 CSV; NONE = yok)",      null),
        new("auth.min_steam_account_age_days",            "age_verification",     "Steam hesap minimum yaş eşiği",                             "gün"),

        // --- Reputation thresholds (T43 — 02 §13, 06 §3.1) ---
        new("reputation.min_account_age_days",            "reputation",           "Hesap yaşı eşiği — altında reputationScore null",           "gün"),
        new("reputation.min_completed_transactions",      "reputation",           "Tamamlanmış işlem eşiği — altında reputationScore null",    "adet"),
    ];
}

/// <summary>One row of <see cref="SystemSettingsCatalog.All"/>.</summary>
public sealed record SystemSettingMetadata(
    string Key,
    string ApiCategory,
    string Label,
    string? Unit);
