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
    SANCTIONS_MATCH
}
