namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Stable message keys for the dispute auto-checkers (T58 — 02 §10.1, 03 §6.1–§6.3)
/// and their localized renderings (WP17 i18n).
/// </summary>
/// <remarks>
/// <para>
/// The auto-checkers return a stable <c>MessageKey</c> instead of a hardcoded
/// Turkish sentence. <see cref="Localize"/> renders that key in the disputing
/// buyer's locale at the point the dispute service stores it, returns it on the
/// open/submit-tx-hash response, and feeds it to the <c>DISPUTE_RESULT</c>
/// notification <c>{Outcome}</c> parameter — so the buyer (the single recipient
/// of the auto-resolved notification and the viewer of the open response) sees
/// the result in their own language. The English entry is the fallback when a
/// locale is missing the key (05 §7.3 fallback rule).
/// </para>
/// <para>
/// Renderings intentionally carry no trailing period: the consuming surfaces —
/// the <c>DISPUTE_RESULT</c> body ("...: {Outcome}.") and the UI label — supply
/// their own punctuation, matching the previous hardcoded strings.
/// </para>
/// </remarks>
public static class DisputeAutoCheckMessages
{
    public const string PaymentResolved = "PAYMENT_RESOLVED";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string DeliveryDelivered = "DELIVERY_DELIVERED";
    public const string DeliveryOfferActive = "DELIVERY_OFFER_ACTIVE";
    public const string DeliveryNotStarted = "DELIVERY_NOT_STARTED";
    public const string WrongItemMatch = "WRONG_ITEM_MATCH";
    public const string WrongItemMismatch = "WRONG_ITEM_MISMATCH";
    public const string WrongItemNoDelivery = "WRONG_ITEM_NO_DELIVERY";

    /// <summary>Response shown to the buyer after a manual escalation (07 §7.10).</summary>
    public const string ManualEscalated = "MANUAL_ESCALATED";

    private const string DefaultLocale = "en";

    // locale → (key → rendering). English is the canonical fallback (05 §7.3).
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "Your payment was verified; the transaction continues",
                [PaymentNotFound] = "No payment was found on the blockchain",
                [DeliveryDelivered] = "The item has been delivered to your inventory",
                [DeliveryOfferActive] = "Your trade offer is active — please accept it on Steam",
                [DeliveryNotStarted] = "The trade offer has not been created yet; the delivery stage has not been reached",
                [WrongItemMatch] = "The delivered item matches the item in the transaction",
                [WrongItemMismatch] = "The delivered item does not match the expected item — your transaction has been put under review",
                [WrongItemNoDelivery] = "No delivery data was found; the delivery stage has not been reached",
                [ManualEscalated] = "Your dispute has been forwarded to the admin team",
            },
            ["tr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "Ödemeniz doğrulandı, işlem devam ediyor",
                [PaymentNotFound] = "Blockchain üzerinde ödeme bulunamadı",
                [DeliveryDelivered] = "Item envanterinize teslim edilmiş durumda",
                [DeliveryOfferActive] = "Trade offer'ınız aktif, lütfen Steam üzerinden kabul edin",
                [DeliveryNotStarted] = "Trade offer henüz oluşturulmadı; teslim aşamasına gelinmedi",
                [WrongItemMatch] = "Teslim edilen item, işlemdeki item ile eşleşiyor",
                [WrongItemMismatch] = "Teslim edilen item beklenen item ile eşleşmiyor — işleminiz incelemeye alındı",
                [WrongItemNoDelivery] = "Teslim verisi bulunamadı; teslim aşamasına gelinmedi",
                [ManualEscalated] = "İtirazınız admin ekibine iletildi",
            },
            ["es"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "Tu pago fue verificado; la transacción continúa",
                [PaymentNotFound] = "No se encontró ningún pago en la blockchain",
                [DeliveryDelivered] = "El artículo ha sido entregado en tu inventario",
                [DeliveryOfferActive] = "Tu oferta de intercambio está activa — acéptala en Steam",
                [DeliveryNotStarted] = "La oferta de intercambio aún no se ha creado; no se ha llegado a la etapa de entrega",
                [WrongItemMatch] = "El artículo entregado coincide con el artículo de la transacción",
                [WrongItemMismatch] = "El artículo entregado no coincide con el artículo esperado — tu transacción ha sido puesta en revisión",
                [WrongItemNoDelivery] = "No se encontraron datos de entrega; no se ha llegado a la etapa de entrega",
                [ManualEscalated] = "Tu disputa ha sido enviada al equipo de administración",
            },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "您的付款已验证，交易继续进行",
                [PaymentNotFound] = "区块链上未找到付款",
                [DeliveryDelivered] = "物品已送达您的库存",
                [DeliveryOfferActive] = "您的交易报价已激活——请在 Steam 上接受",
                [DeliveryNotStarted] = "交易报价尚未创建；尚未进入交付阶段",
                [WrongItemMatch] = "已交付的物品与交易中的物品一致",
                [WrongItemMismatch] = "已交付的物品与预期物品不符——您的交易已进入审核",
                [WrongItemNoDelivery] = "未找到交付数据；尚未进入交付阶段",
                [ManualEscalated] = "您的争议已转交给管理团队",
            },
        };

    /// <summary>
    /// Render <paramref name="messageKey"/> in <paramref name="locale"/>
    /// (e.g. "en", "tr-TR"), falling back to English then to the key itself.
    /// </summary>
    public static string Localize(string messageKey, string? locale)
    {
        var lang = NormalizeLocale(locale);
        if (Translations.TryGetValue(lang, out var byKey)
            && byKey.TryGetValue(messageKey, out var text))
        {
            return text;
        }

        return Translations[DefaultLocale].TryGetValue(messageKey, out var fallback)
            ? fallback
            : messageKey;
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return DefaultLocale;

        var dash = locale.IndexOf('-');
        var lang = dash > 0 ? locale[..dash] : locale;
        return Translations.ContainsKey(lang) ? lang : DefaultLocale;
    }
}
