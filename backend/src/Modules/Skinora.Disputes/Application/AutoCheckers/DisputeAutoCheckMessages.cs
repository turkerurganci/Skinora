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

    /// <summary>
    /// v3.0 — the item left the seller's inventory but never arrived in the
    /// buyer's (02 §9.2 misdelivery signature). Replaces the retired
    /// <c>DELIVERY_OFFER_ACTIVE</c>: the platform sends no trade offer, so
    /// telling the buyer to accept one on Steam was both impossible to act on
    /// and reassuring in exactly the case that warrants an admin (02 §10.1).
    /// </summary>
    public const string DeliveryAssetGoneNotArrived = "DELIVERY_ASSET_GONE_NOT_ARRIVED";

    /// <summary>
    /// v3.0 — no delivery evidence at all. Replaces <c>DELIVERY_NOT_STARTED</c>,
    /// whose wording described a platform-created trade offer that no longer
    /// exists; in P2P the seller sends the trade themselves.
    /// </summary>
    public const string DeliveryNotSent = "DELIVERY_NOT_SENT";

    /// <summary>
    /// T130 — 03 §6.2 Sonuç D. The buyer's inventory is hidden or Steam could
    /// not be read, so nothing may be concluded either way (08 §2.3). Before
    /// T130 this case fell through to <see cref="DeliveryNotSent"/>, which told
    /// the buyer the seller had not sent — a negative finding about a seller the
    /// platform had made no observation about.
    /// </summary>
    public const string DeliveryInventoryUnreadable = "DELIVERY_INVENTORY_UNREADABLE";

    /// <summary>
    /// T130 — 03 §6.2 Sonuç E. The inventory conjunction held but the launch
    /// gate is closed (DEPLOY_RUNBOOK §H), so the evidence is real and under
    /// review while no money moves on it. The dispute stays OPEN and
    /// escalatable.
    /// </summary>
    public const string DeliveryEvidenceUnderReview = "DELIVERY_EVIDENCE_UNDER_REVIEW";

    public const string WrongItemMatch = "WRONG_ITEM_MATCH";
    public const string WrongItemMismatch = "WRONG_ITEM_MISMATCH";
    public const string WrongItemNoDelivery = "WRONG_ITEM_NO_DELIVERY";

    /// <summary>
    /// T130 — the wrong-item twin of <see cref="DeliveryInventoryUnreadable"/>.
    /// 03 §6.3 compares what arrived against the transaction's item; an
    /// unreadable inventory closes that comparison without saying anything about
    /// what the seller did.
    /// </summary>
    public const string WrongItemInventoryUnreadable = "WRONG_ITEM_INVENTORY_UNREADABLE";

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
                [DeliveryAssetGoneNotArrived] = "The item has left the seller's inventory but has not arrived in yours — your transaction has been put under review",
                [DeliveryNotSent] = "The seller does not appear to have sent the item yet",
                [DeliveryInventoryUnreadable] = "Your inventory could not be read — make it public, or use the \"I received it\" button if the item has arrived",
                [DeliveryEvidenceUnderReview] = "Delivery evidence was found and your transaction is being reviewed",
                [WrongItemMatch] = "The delivered item matches the item in the transaction",
                [WrongItemMismatch] = "The delivered item does not match the expected item — your transaction has been put under review",
                [WrongItemNoDelivery] = "No delivery data was found; the delivery stage has not been reached",
                [WrongItemInventoryUnreadable] = "Your inventory could not be read, so the delivered item could not be compared — make it public and try again",
                [ManualEscalated] = "Your dispute has been forwarded to the admin team",
            },
            ["tr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "Ödemeniz doğrulandı, işlem devam ediyor",
                [PaymentNotFound] = "Blockchain üzerinde ödeme bulunamadı",
                [DeliveryDelivered] = "Item envanterinize teslim edilmiş durumda",
                [DeliveryAssetGoneNotArrived] = "Item satıcının envanterinden çıkmış ama sizinkine ulaşmamış — işleminiz incelemeye alındı",
                [DeliveryNotSent] = "Satıcı item'ı henüz göndermemiş görünüyor",
                [DeliveryInventoryUnreadable] = "Envanteriniz okunamadı — envanterinizi herkese açık yapın veya item'ı aldıysanız \"Teslim aldım\" butonunu kullanın",
                [DeliveryEvidenceUnderReview] = "Teslimat kanıtı bulundu, işleminiz inceleniyor",
                [WrongItemMatch] = "Teslim edilen item, işlemdeki item ile eşleşiyor",
                [WrongItemMismatch] = "Teslim edilen item beklenen item ile eşleşmiyor — işleminiz incelemeye alındı",
                [WrongItemNoDelivery] = "Teslim verisi bulunamadı; teslim aşamasına gelinmedi",
                [WrongItemInventoryUnreadable] = "Envanteriniz okunamadığı için teslim edilen item karşılaştırılamadı — envanterinizi herkese açık yapıp tekrar deneyin",
                [ManualEscalated] = "İtirazınız admin ekibine iletildi",
            },
            ["es"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "Tu pago fue verificado; la transacción continúa",
                [PaymentNotFound] = "No se encontró ningún pago en la blockchain",
                [DeliveryDelivered] = "El artículo ha sido entregado en tu inventario",
                [DeliveryAssetGoneNotArrived] = "El artículo salió del inventario del vendedor pero no llegó al tuyo — tu transacción ha sido puesta en revisión",
                [DeliveryNotSent] = "El vendedor aún no parece haber enviado el artículo",
                [DeliveryInventoryUnreadable] = "No se pudo leer tu inventario — hazlo público, o usa el botón \"Lo he recibido\" si el artículo ya llegó",
                [DeliveryEvidenceUnderReview] = "Se encontraron pruebas de entrega y tu transacción está siendo revisada",
                [WrongItemMatch] = "El artículo entregado coincide con el artículo de la transacción",
                [WrongItemMismatch] = "El artículo entregado no coincide con el artículo esperado — tu transacción ha sido puesta en revisión",
                [WrongItemNoDelivery] = "No se encontraron datos de entrega; no se ha llegado a la etapa de entrega",
                [WrongItemInventoryUnreadable] = "No se pudo leer tu inventario, así que no se pudo comparar el artículo entregado — hazlo público e inténtalo de nuevo",
                [ManualEscalated] = "Tu disputa ha sido enviada al equipo de administración",
            },
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymentResolved] = "您的付款已验证，交易继续进行",
                [PaymentNotFound] = "区块链上未找到付款",
                [DeliveryDelivered] = "物品已送达您的库存",
                [DeliveryAssetGoneNotArrived] = "物品已离开卖家库存但未到达您的库存——您的交易已进入审核",
                [DeliveryNotSent] = "卖家似乎尚未发送物品",
                [DeliveryInventoryUnreadable] = "无法读取您的库存——请将其设为公开，或在物品已送达时点击\"我已收到\"按钮",
                [DeliveryEvidenceUnderReview] = "已找到交付证据，您的交易正在审核中",
                [WrongItemMatch] = "已交付的物品与交易中的物品一致",
                [WrongItemMismatch] = "已交付的物品与预期物品不符——您的交易已进入审核",
                [WrongItemNoDelivery] = "未找到交付数据；尚未进入交付阶段",
                [WrongItemInventoryUnreadable] = "无法读取您的库存，因此无法比对已交付的物品——请将其设为公开后重试",
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
