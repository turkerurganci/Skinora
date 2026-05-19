# GeoIP Setup Runbook — T83

Operatör kılavuzu — MaxMind GeoLite2-Country MMDB indirme + mount + güncelleme. Referans: 02 §21.1, 03 §11a.1, 08 §10.

---

## §1 MaxMind Hesap Açma + License Key

1. <https://www.maxmind.com/en/geolite2/signup> adresinden ücretsiz hesap aç.
2. Onay e-postası gelir → Account Dashboard → **Manage License Keys** → **Generate new license key**.
3. License key'i kayıt altına al — `MAXMIND_LICENSE_KEY` env değişkeni (operatör tarafı, asla kod repo'sunda saklanmaz).

License sözleşmesi MaxMind GeoLite2 EULA — ücretsiz dağıtım kısıtlı (commercial use OK, redistribution NO). MMDB dosyası **repo'ya commit edilmez**.

---

## §2 MMDB Dosyasını İndirme

İlk kurulum + aylık güncelleme komutu:

```bash
# UNIX:
LICENSE_KEY="<your-license-key>"
TARGET_DIR="/var/lib/skinora/geoip"
sudo mkdir -p "$TARGET_DIR"
curl -L -o "$TARGET_DIR/GeoLite2-Country.tar.gz" \
  "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country&license_key=$LICENSE_KEY&suffix=tar.gz"
sudo tar -xzf "$TARGET_DIR/GeoLite2-Country.tar.gz" -C "$TARGET_DIR" --strip-components=1 --wildcards '*/GeoLite2-Country.mmdb'
sudo chown skinora:skinora "$TARGET_DIR/GeoLite2-Country.mmdb"
sudo rm "$TARGET_DIR/GeoLite2-Country.tar.gz"
ls -la "$TARGET_DIR/"
# Expected: -rw-r--r-- skinora skinora ~6MB GeoLite2-Country.mmdb
```

```powershell
# Windows:
$LicenseKey = "<your-license-key>"
$TargetDir = "C:\ProgramData\Skinora\geoip"
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
Invoke-WebRequest -Uri "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country&license_key=$LicenseKey&suffix=tar.gz" `
  -OutFile "$TargetDir\GeoLite2-Country.tar.gz"
tar -xzf "$TargetDir\GeoLite2-Country.tar.gz" -C $TargetDir --strip-components=1 --wildcards '*/GeoLite2-Country.mmdb'
Remove-Item "$TargetDir\GeoLite2-Country.tar.gz"
```

---

## §3 Backend'e Bağlama

`appsettings.json` (veya production env override):

```jsonc
"Geolocation": {
  "DatabasePath": "/var/lib/skinora/geoip/GeoLite2-Country.mmdb"
}
```

Veya env değişkeni ile:

```bash
SKINORA_Geolocation__DatabasePath=/var/lib/skinora/geoip/GeoLite2-Country.mmdb
```

Docker Compose örnek:

```yaml
backend:
  volumes:
    - /var/lib/skinora/geoip:/data/geoip:ro
  environment:
    Geolocation__DatabasePath: /data/geoip/GeoLite2-Country.mmdb
```

---

## §4 Doğrulama

Backend startup log'unda görmeyi bekle:

```
MaxMind GeoLite2 resolver enabled (db: /data/geoip/GeoLite2-Country.mmdb).
```

Eğer MMDB yok / okunamaz / hatalı:

```
MaxMind GeoLite2 database not configured (Geolocation:DatabasePath empty or missing); header-only resolution.
```

Bu fail-open davranıştır — geo-block katmanı `X-Country-Code` header'ına düşer (Cloudflare CF-IPCountry vb.).

End-to-end test (curl):

```bash
# Yasaklı ülke listesini güncel hale getir (admin yetkisi gerekli):
# Admin UI: /admin/settings → "Erişim ve Uyumluluk" → "Yasaklı ülke listesi" → "IR,KP,CU"

# Bilinen yasaklı IP ile login dene (örn. İran ASN):
curl -v "https://api.skinora.com/api/v1/auth/steam/callback?<openid-params>"
# Expected: 302 → /auth/callback?error=geo_blocked
```

---

## §5 VPN/Proxy Sinyali (Opsiyonel)

VPN/proxy sinyali default kapalıdır. Aktivasyon için:

```jsonc
"VpnDetection": {
  "Enabled": true,
  "TorExitListUrl": "https://check.torproject.org/torbulkexitlist",
  "CacheDurationMinutes": 60,
  "RefreshTimeoutSeconds": 10
}
```

Aktive edildiğinde:
- İlk login'de Tor exit list fetch'lenir + 1 saat cache'lenir.
- IP listede ise `UserLoginLog.HasVpnSignal=true` yazılır.
- Login **bloke olmaz** — sadece kayıt edilir.
- Tor exit list outage'da `false` döner (soft fail).

---

## §6 Periyodik Bakım

| Görev | Sıklık | Komut |
|---|---|---|
| MaxMind MMDB güncelle | Ayda 1 | `§2` komutunu cron'a koy (örn. her ayın 1'i 03:00) |
| MMDB dosya boyut kontrolü | Cron sonrası | `ls -la $TARGET_DIR/GeoLite2-Country.mmdb` — beklenen ~6MB; çok küçükse indirme bozuk |
| Backend restart | MMDB değiştiğinde | Reader process boyunca handle'ı açık tutar — yeni MMDB için restart |
| `auth.banned_countries` güncelleme | İhtiyaç oldukça | Admin UI veya `PUT /api/v1/admin/settings/{id}` |

**Cron örneği** (Linux):

```cron
0 3 1 * * /usr/local/bin/skinora-geoip-update.sh >> /var/log/skinora/geoip-update.log 2>&1
```

`/usr/local/bin/skinora-geoip-update.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
LICENSE_KEY="$(cat /etc/skinora/maxmind.license)"
TARGET_DIR="/var/lib/skinora/geoip"
TMP="$(mktemp -d)"
curl -fL -o "$TMP/db.tar.gz" \
  "https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country&license_key=$LICENSE_KEY&suffix=tar.gz"
tar -xzf "$TMP/db.tar.gz" -C "$TMP" --strip-components=1 --wildcards '*/GeoLite2-Country.mmdb'
install -m 0644 -o skinora -g skinora "$TMP/GeoLite2-Country.mmdb" "$TARGET_DIR/GeoLite2-Country.mmdb"
rm -rf "$TMP"
systemctl restart skinora-backend
```

---

## §7 Sorun Giderme

| Sorun | Olası Neden | Çözüm |
|---|---|---|
| Startup log "header-only resolution" | `DatabasePath` boş veya dosya yok | §2 + §3 adımlarını tekrar et |
| Startup log "MaxMind GeoLite2 reader failed to load" | MMDB bozuk | Dosyayı yeniden indir (§2) |
| Geo-block çalışmıyor (banned ülkeden giriş yapılıyor) | `auth.banned_countries` boş veya `NONE` | Admin UI → settings → liste güncelle |
| `X-Country-Code` header beklenenden farklı | Edge config (Cloudflare/CloudFront) IP geolocation kapalı | Edge config kontrol et |
| Tor sinyali her zaman `false` | `VpnDetection:Enabled=false` (default) | `§5` ile aktive et |
| Tor list 403/timeout | torproject.org outage veya firewall | Soft fail, otomatik recover. Cache içeriği kontrol: log'larda "Tor exit node list refreshed" satırı |

---

## §8 Lisans Notları

- **GeoLite2-Country MMDB** — MaxMind, Inc. EULA. Free for personal/commercial use, **no redistribution**.
- **Test mmdb** (`tests/Skinora.Auth.Tests/TestData/`) — MaxMind public test data, Apache 2.0.
- **Tor exit list** — Public, no auth, polite caching expected (1h+).

---

*Skinora — GeoIP Setup Runbook v1.0 (T83)*
