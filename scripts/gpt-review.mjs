#!/usr/bin/env node
/**
 * GPT Cross-Review Script
 * Usage: node gpt-review.mjs <doc-path> [--round N] [--dry-run]
 *
 * Sends a Skinora project document to OpenAI o3 for structured review.
 * Writes each round to Docs/GPT_REVIEW_REPORTS/ and prints to stdout.
 *
 * --dry-run: Only prints to stdout, does not write report files.
 */

import { readFileSync, writeFileSync, existsSync, mkdirSync } from "fs";
import { basename, resolve, dirname } from "path";
import OpenAI from "openai";

// --- CLI args ---
const args = process.argv.slice(2);
const dryRun = args.includes("--dry-run");
const roundFlag = args.indexOf("--round");
const round = roundFlag !== -1 ? parseInt(args[roundFlag + 1]) : 1;
const docPath = args.find(a => !a.startsWith("--") && (roundFlag === -1 || args.indexOf(a) !== roundFlag + 1));

if (!docPath) {
  console.error("Usage: node gpt-review.mjs <doc-path> [--round N] [--dry-run]");
  process.exit(1);
}

if (!process.env.OPENAI_API_KEY) {
  console.error("❌ OPENAI_API_KEY environment variable is not set.");
  process.exit(1);
}

const docContent = readFileSync(docPath, "utf-8");
const docName = basename(docPath);
const docNum = docName.match(/^(\d+)/)?.[1] || "XX";

// Report output directory
const reportsDir = resolve(dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Z]:)/i, "$1")), "..", "Docs", "GPT_REVIEW_REPORTS");
if (!dryRun && !existsSync(reportsDir)) mkdirSync(reportsDir, { recursive: true });

const client = new OpenAI();
const modelName = process.env.REVIEW_MODEL || "o3";

const systemPrompt = `Sen deneyimli bir software architect ve technical product review uzmanısın.
Görevin: Bir CS2 item escrow platformu (Skinora) projesinin dokümanlarını review etmek.

## Review Kriterleri
1. **Tutarlılık** — İç çelişki var mı? Bir yerde söylenen başka yerde çelişiyor mu?
2. **Eksiklik** — Ele alınması gereken ama atlanmış konu var mı?
3. **Belirsizlik** — "Muhtemelen", "belki", "olabilir" gibi belirsiz ifadeler var mı? Net olmayan tanımlar?
4. **Teknik Doğruluk** — Teknik ifadeler doğru mu? Gerçekleştirilebilir mi?
5. **Edge Case** — Düşünülmemiş uç durumlar var mı?
6. **Güvenlik** — Güvenlik açığına yol açabilecek eksik tanımlar var mı?
7. **UX/Kullanılabilirlik** — Kullanıcı deneyimi açısından sorunlu akışlar var mı?

## Çıktı Formatı
Her bulgu için şu yapıyı kullan:

### BULGU-{N}: {kısa başlık}
- **Seviye:** KRİTİK | ORTA | DÜŞÜK
- **Kategori:** Tutarlılık | Eksiklik | Belirsizlik | Teknik | Edge Case | Güvenlik | UX
- **Konum:** Dokümanın hangi bölümü (section numarası veya başlığı)
- **Sorun:** Ne yanlış veya eksik
- **Öneri:** Nasıl düzeltilmeli

Eğer doküman iyi durumdaysa ve kritik bulgu yoksa:
### SONUÇ: TEMİZ
Doküman mevcut haliyle yeterli kalitede.

## Kurallar
- Sadece gerçek, somut sorunları raporla. Nitpicking yapma.
- "Şunu da ekleyebilirsiniz" tarzı nice-to-have öneriler verme — sadece gerçek eksiklik/hataları bildir.
- Kozmetik/format önerisi yapma.
- Bulgu yoksa TEMİZ de, gereksiz bulgu üretme.
- Türkçe yaz.`;

const userPrompt = `## Review Round ${round}

Aşağıdaki dokümanı review et: **${docName}**

${round > 1 ? "Bu doküman önceki review'lardan gelen bulgulara göre düzeltildi. Yeniden review et — hem eski bulguların düzgün çözülüp çözülmediğini kontrol et, hem de yeni sorunlar ara." : "Bu dokümanın ilk review'ı."}

---

${docContent}`;

console.log(`🔍 GPT Cross-Review başlatılıyor: ${docName} (Round ${round})`);
console.log(`📡 Model: OpenAI ${modelName}`);
if (dryRun) console.log(`⚡ DRY RUN — rapor dosyasına yazılmayacak`);
console.log("---\n");

try {
  const response = await client.chat.completions.create({
    model: modelName,
    messages: [
      { role: "system", content: systemPrompt },
      { role: "user", content: userPrompt },
    ],
  });

  const result = response.choices[0].message.content;
  const usage = response.usage;
  const tokens = usage
    ? `input: ${usage.prompt_tokens}, output: ${usage.completion_tokens}, total: ${usage.total_tokens}`
    : "N/A";

  // Print to stdout
  console.log(result);
  console.log("\n---");
  console.log(`✅ Review tamamlandı. Tokens: ${tokens}`);

  if (dryRun) {
    console.log("⚡ DRY RUN — rapor dosyası atlandı.");
    process.exit(0);
  }

  // Build report content
  const now = new Date().toISOString().slice(0, 19).replace("T", " ");
  const isClean = /SONUÇ:\s*TEMİZ/i.test(result);
  const findingCount = (result.match(/### BULGU-/g) || []).length;

  const reportContent = `# GPT Cross-Review Raporu — ${docName}

**Tarih:** ${now}
**Model:** OpenAI ${modelName}
**Round:** ${round}
**Token Kullanımı:** ${tokens}
**Sonuç:** ${isClean ? "✅ TEMİZ" : `⚠️ ${findingCount} bulgu`}

---

## GPT Çıktısı

${result}

---

## Claude Bağımsız Değerlendirmesi

> _Bu bölüm Claude tarafından **bağımsız ve objektif** olarak doldurulacak._
> _Claude, GPT'nin her bulgusunu proje bağlamı ve diğer dokümanlarla çapraz kontrol ederek değerlendirir._
> _Karar: ✅ KABUL (GPT haklı) | ❌ RET (GPT yanlış) | ⚠️ KISMİ (kısmen haklı, farklı çözüm)_

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
${isClean ? "| - | TEMİZ | ✅ Onay | Doküman her iki AI tarafından da yeterli bulundu | - |" : Array.from({ length: findingCount }, (_, i) => `| ${i + 1} | BULGU-${i + 1} | ⏳ | _Bağımsız analiz bekleniyor_ | |`).join("\n")}

### Claude'un Ek Bulguları

> _GPT'nin kaçırdığı ama Claude'un tespit ettiği sorunlar (varsa):_

_(Henüz değerlendirilmedi)_

---

## Kullanıcı Onayı

- [ ] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [ ] Düzeltmeler uygulandı
${!isClean ? `- [ ] Round ${round + 1} tetiklendi` : "- [x] Döngü tamamlandı — her iki AI de dokümanı onayladı"}
`;

  // Write report file (append round number if round > 1)
  const reportFileName = round === 1
    ? `${docNum}_GPT_REVIEW.md`
    : `${docNum}_GPT_REVIEW_R${round}.md`;
  const reportPath = resolve(reportsDir, reportFileName);
  writeFileSync(reportPath, reportContent, "utf-8");
  console.log(`📄 Rapor yazıldı: Docs/GPT_REVIEW_REPORTS/${reportFileName}`);

} catch (err) {
  console.error(`❌ Hata: ${err.message}`);
  process.exit(1);
}
