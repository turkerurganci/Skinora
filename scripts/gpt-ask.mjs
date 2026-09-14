#!/usr/bin/env node
/**
 * /gorus transport — ask ChatGPT one approved question and bring the answer back.
 *
 * Usage:
 *   node scripts/gpt-ask.mjs --question <file.md> --slug <slug> [options]
 *   node scripts/gpt-ask.mjs --resume Docs/GPT_OPINIONS/2026-09-03_foo.md
 *
 * Options:
 *   --model <name>        Model override (codex: gpt-5.6-sol|terra|luna; api: REVIEW_MODEL)
 *   --transport <t>       Force one of: codex | api | manual  (default: try in that order)
 *   --effort <e>          Codex reasoning effort (default: high)
 *   --timeout <seconds>   Per-transport timeout (default: 300)
 *   --dry-run             Print the prompt and transport readiness; send nothing
 *
 * Exit codes describe TRANSPORT only, never the content of the answer:
 *   0   answer obtained and recorded
 *   10  manual paste pending — finish with --resume
 *   5   question rejected before sending (secret pattern / size)
 *   1   every transport failed, or a usage error
 *
 * There is deliberately no exit code for "GPT disagreed". Reading the answer
 * and deciding what to do is the owner's job; a machine-readable verdict would
 * invite the script — or Claude — to act on it, which is exactly the authority
 * this tool is not given.
 */

import { readFileSync, writeFileSync, existsSync, mkdirSync } from "fs";
import { resolve, basename } from "path";
import { argv } from "process";
import { fileURLToPath } from "url";
import { fromRepoRoot } from "./lib/repo-root.mjs";
import { scanForSecrets } from "./lib/secret-guard.mjs";
import { askCodex, codexAvailable, codexLoginStatus } from "./lib/codex-transport.mjs";
import { askOpenAI } from "./lib/openai-chat.mjs";
import { copyToClipboard } from "./lib/clipboard.mjs";

const MAX_QUESTION_CHARS = 24000;

export const SYSTEM_PROMPT = `Sen deneyimli, bagimsiz bir software architect'sin. Sana bir muhendislik karari hakkinda kisa bir brief veriliyor ve ikinci gorusun isteniyor.

## Nasil cevap ver
- Once net gorusunu soyle: karar dogru mu, kismen mi, yanlis mi. Tek cumle.
- Sonra itirazlarini onem sirasiyla yaz. Her biri icin: hangi varsayim sorunlu, neden sorunlu, ne yapilmali.
- Kararin gozden kacirdigi riskleri ayrica yaz.
- Brief'te hic konusulmamis ama onemli bir nokta varsa "Kacirilan nokta" basligi altinda soyle.
- Daha iyi bir alternatif varsa tek paragrafta anlat.
- En sonda guven derecen: YUKSEK / ORTA / DUSUK ve tek cumlelik gerekcesi.

## Kurallar
- Projeyi bilmiyorsun; yalnizca brief'te yazanla calis. Eksik bilgi varsa "su bilgi olmadan bunu degerlendiremem" de, uydurma.
- Nezaket icin katilma. Karar dogruysa dogru de ve neden dogru oldugunu soyle; yanlissa cekinmeden soyle.
- Kozmetik/stil onerisi verme; yalnizca karari degistirebilecek seyleri yaz.
- Turkce yaz.
- Yalnizca metin cevabi ver. Komut calistirma, dosya okuma/yazma girisiminde bulunma.`;

function usage(msg) {
  if (msg) console.error(`HATA: ${msg}\n`);
  console.error(
    "Kullanim: node scripts/gpt-ask.mjs --question <dosya> --slug <slug> [--model M] [--transport codex|api|manual] [--effort E] [--timeout S] [--dry-run]\n" +
      "          node scripts/gpt-ask.mjs --resume <kayit.md>",
  );
  process.exit(1);
}

function parseArgs(argv) {
  const opts = { effort: "high", timeout: 300 };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => {
      const v = argv[++i];
      if (v === undefined) usage(`${a} icin deger verilmedi`);
      return v;
    };
    switch (a) {
      case "--question": opts.question = next(); break;
      case "--slug": opts.slug = next(); break;
      case "--model": opts.model = next(); break;
      case "--transport": opts.transport = next(); break;
      case "--effort": opts.effort = next(); break;
      // Validated, not just parsed: `--timeout abc` yields NaN, and a NaN
      // timeout does not fail — it silently disables the deadline, so a hung
      // transport would wait forever instead of falling through to the next.
      case "--timeout": {
        const raw = next();
        opts.timeout = parseInt(raw, 10);
        if (!Number.isFinite(opts.timeout) || opts.timeout <= 0) {
          usage(`--timeout pozitif bir saniye degeri olmali: ${raw}`);
        }
        break;
      }
      case "--resume": opts.resume = next(); break;
      case "--dry-run": opts.dryRun = true; break;
      case "--allow-hex": opts.allowHex = true; break;
      default: usage(`bilinmeyen argüman: ${a}`);
    }
  }
  // A misspelled transport must not fall through to a different one: the owner
  // approved the text for a route they named, and silently taking another is
  // the same class of mistake this whole tool exists to avoid.
  if (opts.transport && !TRANSPORTS.includes(opts.transport)) {
    usage(`gecersiz --transport: ${opts.transport} (gecerli: ${TRANSPORTS.join(" | ")})`);
  }
  return opts;
}

const TRANSPORTS = ["codex", "api", "manual"];

const opinionsDir = () => fromRepoRoot("Docs", "GPT_OPINIONS");

function ensureOpinionsDir() {
  const dir = opinionsDir();
  if (!existsSync(dir)) mkdirSync(dir, { recursive: true });
  return dir;
}

/**
 * LOCAL date, not UTC. The record's filename is what a person types and reads;
 * between 00:00 and 03:00 Turkish time a UTC stamp files today's question
 * under yesterday, so the name the skill prints and the file on disk drift.
 */
function today() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function stamp() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${today()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

/**
 * Record path for a slug: Docs/GPT_OPINIONS/YYYY-MM-DD_<slug>.md
 *
 * Never overwrites: a second question with the same slug on the same day gets
 * `-2`, `-3`, ... A record can already carry the owner's decision — and may be
 * the only trace of an opinion they acted on — so silently replacing one would
 * destroy exactly the evidence this directory exists to keep.
 */
function recordPath(slug) {
  const dir = ensureOpinionsDir();
  const base = `${today()}_${slug}`;
  let candidate = resolve(dir, `${base}.md`);
  for (let n = 2; existsSync(candidate); n++) {
    candidate = resolve(dir, `${base}-${n}.md`);
  }
  return candidate;
}

/**
 * Machine-readable fences around the question.
 *
 * The first version delimited it with `---` and read it back with a non-greedy
 * match, which truncated at the question's own first horizontal rule or
 * markdown table separator — and then presented the surviving fragment under
 * the heading "the text the owner approved". Fences that cannot occur in
 * ordinary prose remove the failure entirely.
 *
 * Exported, and imported by the test: re-declaring a constant on the test side
 * is how a suite goes green while measuring nothing (the wizardDraft lesson).
 */
export const QUESTION_START = "<!-- SORU:BASLANGIC -->";
export const QUESTION_END = "<!-- SORU:BITIS -->";

/** Read back the exact approved question from a record file. */
export function extractQuestion(recordText) {
  const start = recordText.indexOf(QUESTION_START);
  const end = recordText.indexOf(QUESTION_END);
  if (start === -1 || end === -1 || end < start) return null;
  return recordText.slice(start + QUESTION_START.length, end).trim();
}

export function buildRecord({ slug, question, answer, source, model, durationMs, usage: tokenUsage }) {
  const duration = durationMs != null ? `${(durationMs / 1000).toFixed(1)} sn` : "—";
  return `# GPT Görüşü — ${slug}

**Tarih:** ${stamp()}
**Kaynak:** ${source}
**Model:** ${model || "—"}
**Süre:** ${duration}${tokenUsage ? `
**Token:** ${tokenUsage}` : ""}

---

## Gönderilen soru (proje sahibinin onayladığı metin)

${QUESTION_START}

${question}

${QUESTION_END}

---

## GPT Cevabı (birebir)

${answer}

---

## Proje Sahibinin Kararı

> _Karar proje sahibine aittir. Claude bu bölümü kendi başına doldurmaz — sahibi ne yapılacağını söyledikten sonra tek satırla buraya yazılır._

⏳ Bekleniyor
`;
}

/** Index row points at the ACTUAL record file, which may carry a -2 suffix. */
function appendIndex(slug, source, recordFile) {
  const indexPath = resolve(opinionsDir(), "README.md");
  if (!existsSync(indexPath)) return;
  const fileName = basename(recordFile);
  const line = `| ${today()} | [${slug}](${fileName}) | ${source} | ⏳ |`;
  const content = readFileSync(indexPath, "utf8");
  const marker = "<!-- INDEX -->";
  if (!content.includes(marker)) return;
  writeFileSync(indexPath, content.replace(marker, `${marker}\n${line}`), "utf8");
}

// --- Transports -------------------------------------------------------------

function tryCodex(prompt, opts) {
  const version = codexAvailable();
  if (!version) return { ok: false, why: "codex CLI kurulu degil (npm i -g @openai/codex)" };
  const status = codexLoginStatus();
  if (!status.ok) {
    return { ok: false, why: `codex girisli degil: ${status.text} — 'codex login --device-auth' calistir` };
  }
  try {
    const r = askCodex({
      prompt,
      model: opts.model,
      effort: opts.effort,
      timeoutMs: opts.timeout * 1000,
    });
    return { ok: true, source: "codex", ...r };
  } catch (err) {
    return { ok: false, why: err.message, authFailure: !!err.authFailure };
  }
}

async function tryApi(question, opts) {
  if (!process.env.OPENAI_API_KEY) return { ok: false, why: "OPENAI_API_KEY tanimli degil" };
  try {
    const started = Date.now();
    const r = await askOpenAI({ system: SYSTEM_PROMPT, user: question, model: opts.model });
    return { ok: true, source: "api", text: r.text, model: r.model, usage: r.usage, durationMs: Date.now() - started };
  } catch (err) {
    return { ok: false, why: err.message };
  }
}

function goManual(prompt, question, slug, questionPath) {
  const record = recordPath(slug);
  let promptFile = record.replace(/\.md$/, ".prompt.md");
  const answerFile = record.replace(/\.md$/, ".answer.md");

  // Never write over the file the owner approved. The skill names questions
  // `<tarih>_<slug>.prompt.md`, which is exactly the path this would pick — so
  // a rerun would replace the approved question with system-prompt + question
  // and the next run would send the system prompt twice.
  if (questionPath && resolve(promptFile) === resolve(questionPath)) {
    promptFile = record.replace(/\.md$/, ".send.md");
  }

  writeFileSync(promptFile, prompt, "utf8");
  writeFileSync(record, buildRecord({
    slug, question,
    answer: "_(manuel yol — cevap henüz yapıştırılmadı)_",
    source: "manuel (bekliyor)", model: "—", durationMs: null,
  }), "utf8");
  appendIndex(slug, "manuel (bekliyor)", record);

  const tool = copyToClipboard(prompt);

  console.log("\n📋 MANUEL YOL — otomatik taşıyıcılar kullanılamadı.\n");
  console.log(tool ? `Soru panoya kopyalandı (${tool}).` : "Pano aracı bulunamadı — soruyu dosyadan kopyala.");
  console.log(`   Soru dosyası : ${promptFile}`);
  console.log("\nYapılacaklar:");
  console.log("   1. ChatGPT'yi aç, soruyu yapıştır.");
  console.log(`   2. Cevabı olduğu gibi şu dosyaya kaydet: ${answerFile}`);
  console.log(`   3. Şunu çalıştır: node scripts/gpt-ask.mjs --resume "${record}"`);
  return 10;
}

function resumeFromAnswer(recordFile) {
  const record = resolve(recordFile);
  if (!existsSync(record)) usage(`kayit dosyasi bulunamadi: ${record}`);
  const answerFile = record.replace(/\.md$/, ".answer.md");
  if (!existsSync(answerFile)) {
    console.error(`HATA: cevap dosyasi yok: ${answerFile}`);
    console.error("ChatGPT cevabini bu dosyaya kaydedip tekrar dene.");
    process.exit(10);
  }
  const answer = readFileSync(answerFile, "utf8").trim();
  if (!answer) {
    console.error(`HATA: cevap dosyasi bos: ${answerFile}`);
    process.exit(10);
  }

  const existing = readFileSync(record, "utf8");
  const question = extractQuestion(existing) ?? "_(kayıttan okunamadı)_";
  const slug = basename(record, ".md").replace(/^\d{4}-\d{2}-\d{2}_/, "");

  writeFileSync(record, buildRecord({
    slug, question, answer,
    source: "manuel", model: "ChatGPT (web)", durationMs: null,
  }), "utf8");

  markIndexAnswered(record);
  console.log(`\n📄 Kayıt güncellendi: ${record}`);
  console.log("\n=== GPT CEVABI ===\n");
  console.log(answer);
  return 0;
}

/** Flip a pending manual row's source once the answer is pasted in. */
function markIndexAnswered(recordFile) {
  const indexPath = resolve(opinionsDir(), "README.md");
  if (!existsSync(indexPath)) return;
  const fileName = basename(recordFile);
  const content = readFileSync(indexPath, "utf8");
  const updated = content
    .split("\n")
    .map((line) =>
      line.includes(`(${fileName})`) ? line.replace("manuel (bekliyor)", "manuel") : line,
    )
    .join("\n");
  if (updated !== content) writeFileSync(indexPath, updated, "utf8");
}

// --- Main -------------------------------------------------------------------

async function main() {
  const opts = parseArgs(process.argv.slice(2));

  if (opts.resume) process.exit(resumeFromAnswer(opts.resume));
  if (!opts.question) usage("--question zorunlu");
  if (!opts.slug) usage("--slug zorunlu");
  if (!/^[a-z0-9][a-z0-9-]{0,48}$/.test(opts.slug)) usage("--slug yalniz kucuk harf, rakam ve tire icermeli");

  const questionPath = resolve(opts.question);
  if (!existsSync(questionPath)) usage(`soru dosyasi bulunamadi: ${questionPath}`);
  const question = readFileSync(questionPath, "utf8").trim();

  if (!question) usage("soru dosyasi bos");
  if (question.length > MAX_QUESTION_CHARS) {
    console.error(`HATA: soru cok uzun (${question.length} > ${MAX_QUESTION_CHARS} karakter). Kisalt.`);
    process.exit(5);
  }

  // Outbound secret guard — runs BEFORE any transport touches the network.
  const { blocking, warnings } = scanForSecrets(question, { allowHex: opts.allowHex });
  for (const w of warnings) {
    console.error(`⚠️  satir ${w.line}: ${w.rule} ${w.hint}`);
  }
  if (blocking.length) {
    console.error("\n❌ SORU GONDERILMEDI — sir kalibi bulundu:\n");
    for (const f of blocking) console.error(`   satir ${f.line}: ${f.rule} ${f.hint}`);
    console.error("\nBunlar dis bir servise gidecekti ve geri alinamazdi. Soruyu temizleyip tekrar dene.");
    process.exit(5);
  }

  const prompt = `${SYSTEM_PROMPT}\n\n---\n\n${question}`;

  if (opts.dryRun) {
    const version = codexAvailable();
    const status = version ? codexLoginStatus() : { ok: false, text: "codex yok" };
    console.log("⚡ DRY RUN — hicbir sey gonderilmedi.\n");
    console.log(`codex           : ${version || "kurulu degil"}`);
    console.log(`codex login     : ${status.ok ? status.text : `HAYIR (${status.text})`}`);
    console.log(`OPENAI_API_KEY  : ${process.env.OPENAI_API_KEY ? "set" : "yok"}`);
    console.log(`soru uzunlugu   : ${question.length} karakter`);
    console.log(`sir taramasi    : ${blocking.length} bloke, ${warnings.length} uyari`);
    console.log("\n--- GONDERILECEK METIN ---\n");
    console.log(prompt);
    process.exit(0);
  }

  const chain = opts.transport ? [opts.transport] : ["codex", "api", "manual"];
  const failures = [];

  for (const transport of chain) {
    if (transport === "manual") {
      process.exit(goManual(prompt, question, opts.slug, questionPath));
    }

    console.log(`📡 Taşıyıcı deneniyor: ${transport}...`);
    const result = transport === "codex" ? tryCodex(prompt, opts) : await tryApi(question, opts);

    if (result.ok) {
      const record = recordPath(opts.slug);
      writeFileSync(record, buildRecord({
        slug: opts.slug,
        question,
        answer: result.text,
        source: result.source,
        model: result.model,
        durationMs: result.durationMs,
        usage: result.usage,
      }), "utf8");
      appendIndex(opts.slug, result.source, record);

      console.log(`\n📄 Kayıt: ${record}`);
      console.log("\n=== GPT CEVABI ===\n");
      console.log(result.text);
      process.exit(0);
    }

    console.log(`   ↳ olmadı: ${result.why}`);
    failures.push(`${transport}: ${result.why}`);
  }

  console.error("\n❌ Hiçbir taşıyıcı çalışmadı:");
  for (const f of failures) console.error(`   - ${f}`);
  console.error("\nManuel yol için: --transport manual");
  process.exit(1);
}

// Only run when invoked as a script — the test file imports SYSTEM_PROMPT and
// buildRecord, and an unguarded main() would fire (and exit) on import.
const invokedDirectly = argv[1] && resolve(argv[1]) === resolve(fileURLToPath(import.meta.url));
if (invokedDirectly) {
  main().catch((err) => {
    console.error(`❌ Beklenmeyen hata: ${err.message}`);
    process.exit(1);
  });
}
