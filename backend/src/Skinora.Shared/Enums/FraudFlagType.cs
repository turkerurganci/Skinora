namespace Skinora.Shared.Enums;

public enum FraudFlagType
{
    PRICE_DEVIATION,
    HIGH_VOLUME,
    ABNORMAL_BEHAVIOR,
    MULTI_ACCOUNT,
    // T82 — yaptırımlı cüzdan adresi eşleşmesi (02 §21.1, 03 §11a.3).
    // Yüksek risk tipi: IFraudFlagService.StageAccountFlagAsync
    // cascadeEmergencyHold = true ile çağrılır → kullanıcının tüm aktif
    // işlemleri EMERGENCY_HOLD'a alınır.
    SANCTIONS_MATCH,

    // T129 — mutabakat sonu kontrolünde trade'in geri alındığının tespiti
    // (02 §4.5.1, §14.2). Hesap düzeyinde açılır: bulgu işlemin değil kişinin
    // hakkındadır ve §14.2 yaptırımı tekrarı sayar. Diğer tiplerden ayrı
    // tutulmasının sebebi tam olarak bu sayım — ABNORMAL_BEHAVIOR torbasına
    // atılsaydı "kaçıncı geri alma" sorusu admin kuyruğunda cevaplanamazdı.
    DELIVERY_REVERSED
}
