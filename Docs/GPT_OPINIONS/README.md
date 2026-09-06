# GPT Görüşleri — anlık ikinci görüş kayıtları

Bu dizin, proje sahibinin **herhangi bir anda, herhangi bir konuda** ChatGPT'den istediği görüşlerin kaydını tutar. Akış `/gorus` skill'iyle çalışır (`.claude/skills/gorus.md`), taşıyıcısı `scripts/gpt-ask.mjs`'dir.

## `GPT_REVIEW_REPORTS/` ile farkı

| | `GPT_OPINIONS/` (bu dizin) | `GPT_REVIEW_REPORTS/` |
|---|---|---|
| Konusu | Tek bir karar / soru | Bir spec dokümanının tamamı |
| Döngü | Tek atış | Round'lar, "SONUÇ: TEMİZ" gelene kadar |
| Kim başlatır | Yalnız proje sahibi | Kalite döngüsü (audit sonrası) da önerir |
| Sonuç kime ait | Karar proje sahibinin | Claude bulguları değerlendirir, sahibi onaylar |
| Skill | `/gorus` | `/gpt-cross-review` |

## Yetki sınırı

Claude bu akışta **karar vermez**. Soruyu hazırlar, **onay alır**, gönderir, cevabı birebir sunar ve durur. Ne yapılacağını proje sahibi söyler. Claude'un kendi görüşü **istendiğinde** verilir — kendiliğinden değil.

## Soru şablonu

```markdown
## Soru
<tek cümle — evet/hayır ya da A/B>

## Bağlam
<≤ 10 satır: dosya:satır + kısa alıntı + ölçülmüş sayılar>

## Mevcut karar / yöntem ve gerekçesi
<≤ 8 madde>

## Elenen alternatifler
| Alternatif | Neden elendi |

## Kabul edilen bedeller / riskler
```

**Sır kuralı:** private key, mnemonic, gerçek parola veya API anahtarı girmez — script göndermeden önce tarar ve bulursa **durur** (`scripts/lib/secret-guard.mjs`, pre-commit hook'un Layer 2 desenlerinin dışa giden yol için portu).

Şekillendirici asimetri: **yanlış pozitif bir düzenlemeye mal olur, yanlış negatif kalıcıdır.** Bu yüzden port, hook'un blokladığı her yerde bloklar ve **üç** noktada ondan **daha katıdır**:

- **64-hex bloke edilir — `0x` önekli hâli dahil.** Tron/EVM private key tam bu şekildedir. Kısa süre uyarı olarak durdu; sonra fark edildi ki uyarı sahibin onayından **sonra** basılıyor ve hiçbir şeyi durdurmuyor — *uyarı kapı değildir*. Tam bir tx hash'i kasten göndermek gerekiyorsa `--allow-hex` ile geçilir; şablon zaten kısaltılmış hash ister (tam hash kayıt commit'inde pre-commit'in 64-hex kuralına da takılır).
- **Markdown tablo hücresindeki değer yakalanır.** Atama kuralı ayırıcı olarak `:` veya `=` ister, boru işaretini saymaz. Bu boşluk burada özellikle ağır: **soru şablonu tabloyu zorunlu kılıyor**, yani bekçinin karşılaşacağı biçim garanti olarak tablodur. Değerin hücreyi **doldurması** aranır (tek kelime, boşluksuz) — `| REDIS_PASSWORD | prod'da ayrı tutulur |` bir anahtarı *anlatır*, taşımaz.
- **Yorum/başlık önekli satırlar elenmez.** Hook bir *diff*'te `#` ile başlayan satırı "anahtar adını anlatan düzyazı" sayar; bir *soru dokümanında* `#` başlıktır, `>` alıntıdır, `--` SQL açar — canlı bir değer oralara yapıştırılabilir.

Ayrıca satırlara bölünmüş / numaralandırılmış mnemonic, değeri bir sonraki satırda duran anahtar ve `JWT_SECRET (prod): <değer>` gibi araya parantez giren biçimler de yakalanır.

> İlk iki madde **doğrulama turunda ölçülerek** eklendi; ikisi de hook'tan miras alınan biçimlerdi ve ilk sürümde açıktı. Ayırt edici ölçüm, tek değişken: `deger: <64hex>` → çıkış 5 (bloke) ama `deger: 0x<64hex>` → çıkış 0 (giderdi); `JWT_SECRET=<parola>` → çıkış 5 ama `| JWT_SECRET | <parola> |` → çıkış 0. Sebep: `\b` sınırı `x` ile ilk hex rakamı arasında hiç oluşmuyor, yani bayrak kuralı EVM cüzdanlarının **kanonik** çıktı biçimiyle deviriliyordu. Ders tanıdık: bir kapının *yapılandırılmış, belgelenmiş ve testli* olması, koruduğunu iddia ettiği değerin oradan geçtiği anlamına gelmiyor.

## Kullanım

```bash
# normal akış (skill Faz 3)
node scripts/gpt-ask.mjs --question Docs/GPT_OPINIONS/2026-09-03_konu.prompt.md --slug konu

# hiçbir şey göndermeden metni ve taşıyıcı durumunu gör
node scripts/gpt-ask.mjs --question <dosya> --slug konu --dry-run

# doğrudan yapıştırma yolu
node scripts/gpt-ask.mjs --question <dosya> --slug konu --transport manual
node scripts/gpt-ask.mjs --resume Docs/GPT_OPINIONS/2026-09-03_konu.md

# soru bilerek tam bir Tron tx hash'i içeriyorsa
node scripts/gpt-ask.mjs --question <dosya> --slug konu --allow-hex
```

Çıkış kodları yalnız taşımayı anlatır: `0` cevap alındı · `10` manuel yapıştırma bekleniyor · `5` soru gönderilmedi (sır/uzunluk) · `1` hiçbir taşıyıcı çalışmadı. **Cevabın içeriğine göre çıkış kodu yoktur** — cevabı okuyan ve karar veren makine değil, proje sahibidir.

Testler:

```bash
node --test scripts/gpt-ask.test.mjs
```

Dosyayı **adıyla** ver: `node --test scripts/` dizini bir paket girişi sanır (`scripts/package.json`'ın `main` alanı yüzünden) ve hiç test koşmadan patlar; `node --test` (kök) ise frontend'in vitest dosyalarını toplayıp yanlış kırmızı verir.

> **Bilinen boşluk — bu testleri hiçbir kapı koşmuyor.** `.github/workflows/ci.yml`'ın `paths-filter` tanımlarında `scripts/**` yok; yalnız `scripts/` altını değiştiren bir PR'da lint/build/test job'larının hepsi *skipped* olur. Yani sır bekçisinin testleri **elle** koşulmadıkça sessizce bayatlayabilir. Bilinçli olarak burada bırakıldı (`/gorus` bir CI işi değil), ama `secret-guard.mjs` değiştirilirse yukarıdaki komut elle koşulmalıdır.

## Taşıyıcılar

1. **codex** — Codex CLI, ChatGPT aboneliğiyle giriş (API anahtarı ve ayrı bakiye gerekmez). OpenAI'nin kendi otomasyon yolu; chatgpt.com'u tarayıcıdan sürmek kullanım şartlarına aykırıdır ve yapılmaz.
2. **api** — `OPENAI_API_KEY` tanımlıysa OpenAI API (`REVIEW_MODEL || o3`). **2026-09-03'te ölçüldü: bu makinede ölü** — anahtar tanımlı ama hesapta bakiye yok (`429 You have no credits remaining`). Yani "anahtar set" ile "API çalışıyor" ayrı şeyler; zincirde kalmasının sebebi bakiye yüklenirse kendiliğinden devreye girmesi.
3. **manuel** — soru panoya kopyalanır, sahibi ChatGPT'ye yapıştırır, cevabı `.answer.md`'ye kaydeder, `--resume` ile akış devam eder.

## Ölçülen kurulum (2026-09-03)

- `@openai/codex` **0.153.0**, Windows 10'da native çalışıyor (`npm install -g @openai/codex`; npm prefix `%APPDATA%\npm` PATH'te olmalı).
- Çalışan çağrı biçimi:
  ```bash
  printf '<soru>' | codex exec - -s read-only --skip-git-repo-check --ephemeral \
    -C <boş dizin> -m gpt-5.6-sol -c model_reasoning_effort='"high"' -o <çıktı.md>
  ```
- **Planlanan `-a/--ask-for-approval` bayrağı bu sürümde YOK** — dokümandan varsayılmıştı, prob düzeltti. Onay/sandbox davranışı `-s read-only` ile kurulur.
- Ajan **boş bir geçici dizinde** çalıştırılır (`-C` + `--skip-git-repo-check`): sahibinin onayladığı metin dışında hiçbir şey modele gitmez ve dosyaya dokunma yüzeyi kalmaz.
- `~/.codex/config.toml` bayat bir `model = "gpt-5.1"` taşıyor; script **her çağrıda `-m` gönderir** (varsayılan `gpt-5.6-sol`, `--model` ile değiştirilir). Bayrak koşullu olsaydı model verilmeyen her soruya bayat model cevap verirdi.

### İkinci ölçüm tuzağı — `codex exec` soruyu ve cevabı stderr'e basıyor

İlk sürüm kimlik hatasını `stdout + stderr` metninde arıyordu. Ölçüldüğünde şu çıktı: `codex exec`, gönderilen **prompt'un tamamını** ve **modelin cevabını** stderr'e yazıyor. Yani aranan metin, hata tanılarını değil sorunun ve cevabın kendisini içeriyordu.

Ayırt edici ölçüm (tek değişken soru metni, sağlayıcı sabit): *"gas fee tahmini doğru mu?"* → **başarılı**; *"JWT refresh token rotasyonu doğru mu?"* → **"oturum geçersiz"** ve alınmış cevap `finally` bloğunda silindi. Bu repoda `refresh token` ve `401 Unauthorized` sıradan konu başlıkları.

Düzeltme iki katmanlı: kimlik kararı artık **çağrının sonucundan** veriliyor (önce cevap dosyası okunur; doluysa çağrı başarılıdır, transkriptte hangi kelime geçerse geçsin), ve yalnız cevap boşken `stderr`'in **codex'e ait `Error:` satırlarında** kimlik hatası aranıyor.

### Ölçüm tuzağı — `codex login status` kullanılabilirliği ÖLÇMÜYOR

Prob sırasında `codex login status` **"Logged in using ChatGPT"** dedi ve `exit 0` döndü; aynı anda gerçek çağrı **401 Unauthorized** ile düştü (token Mart'tan kalmaydı, refresh token bir kez kullanılmıştı). Yani durum komutu "girişli" derken oturum ölüydü.

Script bu yüzden `login status`'u yalnız **ucuz ön eleme** olarak kullanır; gerçek kimlik teşhisi yukarıdaki iki katmanlı yoldan gelir ve *"codex login --device-auth ile yeniden gir"* der. `feedback_verify_probe_subject` ailesinin bir örneği daha: bir kontrolün *yeşil* olması, ölçtüğünü sandığın şeyi ölçtüğü anlamına gelmiyor.

## Kayıtlar

| Tarih | Konu | Kaynak | Karar |
|---|---|---|---|
<!-- INDEX -->
| 2026-09-04 | [gas-fee-rezervasyon-itiraz](2026-09-04_gas-fee-rezervasyon-itiraz.md) | codex | ⏳ |
| 2026-09-03 | [canli-prova](2026-09-03_canli-prova.md) | codex | ⏳ |
