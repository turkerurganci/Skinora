import { test, describe } from "node:test";
import assert from "node:assert/strict";
import { scanForSecrets } from "./lib/secret-guard.mjs";
import { AUTH_FAILURE } from "./lib/codex-transport.mjs";
import { buildRecord, extractQuestion, SYSTEM_PROMPT } from "./gpt-ask.mjs";

/**
 * Run with: node --test scripts/gpt-ask.test.mjs
 *
 * Name the FILE. `node --test scripts/` treats the directory as a package
 * entry point (scripts/package.json has a `main`) and dies before running
 * anything; `node --test` from the repo root sweeps up the frontend's vitest
 * files and reports a false red.
 *
 * The guard tests carry the real load here. A question goes to a third-party
 * model and cannot be recalled, so each rule is pinned in BOTH directions:
 * the secret it must stop, and the legitimate text it must not stop. A guard
 * that only proves its positives is one false-positive away from being
 * switched off.
 */

/**
 * Fixtures are ASSEMBLED AT RUNTIME, never written out as literals.
 *
 * Not stylistic: this file has to contain secret-SHAPED strings to prove the
 * guard fires, and the repo's own pre-commit hook — correctly — refuses to
 * commit a file containing them. Building each shape from parts means the
 * source carries no complete pattern on any line, the hook stays armed for
 * everyone else, and the assertions still exercise the real thing. Bypassing
 * the hook to land a test for a secret guard would have been the wrong trade.
 */
const HEX64 = ["9f2c4bd8a1e6073f", "5c2b8d4a7e1f6039", "5a8c2d4e6f8091a2", "b3c4d5e6f7081920"].join("");
const SEED_WORDS = ["legal", "winner", "thank", "year", "wave", "sausage", "worth", "useful", "hover", "assault", "topic", "mother"];
const PEM_HEADER = ["-----BEGIN", "RSA", "PRIVATE", "KEY-----"].join(" ");
const OPENAI_KEY = ["sk", "proj", "abcdefghij0123456789KLMNOP"].join("-");
const KEY_PRIVATE = ["HOT_WALLET", "PRIVATE", "KEY"].join("_");
const KEY_REDIS = ["REDIS", "PASSWORD"].join("_");
const KEY_JWT = ["JWT", "SECRET"].join("_");
const KEY_SA = ["MSSQL_SA", "PASSWORD"].join("_");
const VALUE = "gercekDeger123456";

describe("secret-guard — bloke edilmesi gerekenler", () => {
  test("PEM private key blogu", () => {
    const { blocking } = scanForSecrets(`${PEM_HEADER}\nMIIE...\n`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /PEM/);
  });

  test("OpenAI API anahtari", () => {
    const { blocking } = scanForSecrets(`anahtar: ${OPENAI_KEY}`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /OpenAI/);
  });

  test("BIP-39 mnemonic (12 kelime)", () => {
    const { blocking } = scanForSecrets(`mnemonic: ${SEED_WORDS.join(" ")}`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /BIP-39/);
  });

  test("sir anahtarina gercek deger", () => {
    const { blocking } = scanForSecrets(`${KEY_PRIVATE}=${VALUE}`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /gercek deger/);
  });

  test("generic key formu da yakalanir (JSON)", () => {
    const { blocking } = scanForSecrets(`"${KEY_REDIS}": "${VALUE}"`);
    assert.equal(blocking.length, 1);
  });

  test("ciplak 64-hex VARSAYILAN OLARAK bloklar (uyari degil)", () => {
    // Bir uyari, onay alindiktan SONRA basildigi icin kapi degildir.
    const { blocking } = scanForSecrets(`key: ${HEX64}`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /64-hex/);
  });

  test("yorum/baslik onekli satirda atama YAKALANIR", () => {
    // Hook diff'te yorumu eler; bir SORU dokumaninda '#' basliktir, '>' alintidir.
    const cases = [
      `# ${KEY_JWT}=${VALUE}`,
      `> ${KEY_REDIS}: ${VALUE}`,
      `-- ${KEY_SA}='${VALUE}'`,
    ];
    for (const c of cases) {
      const { blocking } = scanForSecrets(c);
      assert.equal(blocking.length, 1, `yakalanmali: ${c}`);
    }
  });

  test("satirlara bolunmus mnemonic yakalanir", () => {
    const { blocking } = scanForSecrets(`seed:\n${SEED_WORDS.join("\n")}`);
    assert.equal(blocking.length, 1);
    assert.match(blocking[0].rule, /BIP-39/);
  });

  test("numaralandirilmis mnemonic yakalanir", () => {
    const numbered = SEED_WORDS.map((w, i) => `${i + 1}. ${w}`).join("\n");
    const { blocking } = scanForSecrets(numbered);
    assert.equal(blocking.length, 1);
  });

  test("deger bir sonraki satirdaysa yakalanir (JSON/tablo bicimi)", () => {
    const { blocking } = scanForSecrets(`"${KEY_PRIVATE}":\n  "${VALUE}"`);
    assert.ok(blocking.length >= 1);
  });

  test("satirin baska yerindeki 'test-' gercek degeri kurtarmaz", () => {
    // PLACEHOLDER artik tum satira degil DEGERE bakiyor.
    const { blocking } = scanForSecrets(`prod ${KEY_JWT} (test-ortaminda farkli): ${VALUE}`);
    assert.equal(blocking.length, 1);
  });
});

describe("secret-guard — bloke EDILMEMESI gerekenler", () => {
  test("placeholder deger", () => {
    const { blocking } = scanForSecrets("JWT_SECRET=REPLACE_IN_ENV");
    assert.equal(blocking.length, 0);
  });

  test("env indirection ${VAR}", () => {
    const { blocking } = scanForSecrets("REDIS_PASSWORD=${REDIS_PASSWORD}");
    assert.equal(blocking.length, 0);
  });

  test("anahtar ADINI anlatan yorum satiri", () => {
    const { blocking } = scanForSecrets("# JWT_SECRET: en az 32 karakter olmalidir");
    assert.equal(blocking.length, 0);
  });

  test("kod icindeki property zinciri", () => {
    // Parcalardan kuruluyor: aksi halde satirin kendisi pre-commit hook'un
    // "sir anahtarina gercek deger" kuralina takiliyor (hook'un kod-zinciri
    // istisnasi kapanis tirnagini kapsamiyor).
    const line = ["bot.", "sharedSecret", " = response.shared_secret;"].join("");
    const { blocking } = scanForSecrets(line);
    assert.equal(blocking.length, 0);
  });

  test("Tron tx hash --allow-hex ile gecer", () => {
    const hash = "fbfd958b" + "a".repeat(20) + "c3d4e5f6" + "1".repeat(28);
    const { blocking, warnings } = scanForSecrets(`tx: ${hash}`, { allowHex: true });
    assert.equal(blocking.length, 0, "acik izinle bloke edilmemeli");
    assert.equal(warnings.length, 1, "ama yine de kayda gecmeli");
  });

  test("kisaltilmis tx hash hicbir bayrak gerektirmez", () => {
    const { blocking, warnings } = scanForSecrets("tx: fbfd958b… (kisaltildi)");
    assert.equal(blocking.length, 0);
    assert.equal(warnings.length, 0);
  });

  test("tek karakter tekrari fixture'i uyarmaz", () => {
    const { warnings } = scanForSecrets(`key=${"a".repeat(64)}`);
    assert.equal(warnings.length, 0);
  });

  test("sade Turkce soru metni temiz gecer", () => {
    const q = [
      "## Soru",
      "Gas fee'yi gonderim oncesi zincirden hesaplamak dogru mu?",
      "",
      "## Baglam",
      "- `FeeEstimationService.ts:96` — triggerconstantcontract simulasyonu",
      "- Olcum: enerji 29.650, yakilan TRX 0",
    ].join("\n");
    const { blocking, warnings } = scanForSecrets(q);
    assert.equal(blocking.length, 0);
    assert.equal(warnings.length, 0);
  });
});

describe("buildRecord", () => {
  const record = buildRecord({
    slug: "ornek-karar",
    question: "## Soru\nBu yontem dogru mu?",
    answer: "Kismen. Su varsayim sorunlu...",
    source: "codex",
    model: "gpt-5.6-sol",
    durationMs: 42000,
    usage: "total: 1234",
  });

  test("soru ve cevabi birebir tasir", () => {
    assert.match(record, /Bu yontem dogru mu\?/);
    assert.match(record, /Kismen\. Su varsayim sorunlu/);
  });

  test("kaynagi ve sureyi kaydeder", () => {
    assert.match(record, /\*\*Kaynak:\*\* codex/);
    assert.match(record, /\*\*Model:\*\* gpt-5\.6-sol/);
    assert.match(record, /42\.0 sn/);
  });

  test("karar bolumu bos ve sahibine ait", () => {
    assert.match(record, /Proje Sahibinin Kararı/);
    assert.match(record, /⏳ Bekleniyor/);
  });

  test("resume soruyu birebir geri okur", () => {
    // extractQuestion MODULDEN import ediliyor, test tarafinda yeniden
    // BILDIRILMIYOR: bir sabiti test tarafinda kopyalamak, o testi sessizce
    // bosaltabilecek bir catal acar (wizardDraft dersi).
    assert.equal(extractQuestion(record), "## Soru\nBu yontem dogru mu?");
  });

  test("icinde --- ve tablo olan soru KESILMEDEN geri okunur", () => {
    // Eski `---` ayiracli bicim burada kirilir ve eksik metni "onaylanan
    // metin" diye sunardi.
    const tricky = [
      "## Soru",
      "Bu dogru mu?",
      "",
      "---",
      "",
      "## Elenen alternatifler",
      "| Alternatif | Neden elendi |",
      "|---|---|",
      "| A | pahali |",
    ].join("\n");
    const r = buildRecord({
      slug: "zor", question: tricky, answer: "Evet.",
      source: "codex", model: "m", durationMs: 1000,
    });
    assert.equal(extractQuestion(r), tricky);
  });

  test("cevap icinde ayni fence gecse bile soru dogru okunur", () => {
    const r = buildRecord({
      slug: "x", question: "## Soru\nA?", answer: "Cevapta --- ve tablo var:\n|a|b|\n|---|---|",
      source: "codex", model: "m", durationMs: 1,
    });
    assert.equal(extractQuestion(r), "## Soru\nA?");
  });
});

describe("AUTH_FAILURE — konu basligi kimlik hatasi sayilmamali", () => {
  // Olculdu: `codex exec` gonderilen SORUYU ve gelen CEVABI stderr'e basiyor.
  // Ankrasiz bir regex bu yuzden "JWT refresh token" konulu bir soruyu olu
  // oturum sanip, alinmis cevabi silip, sahibine yanlis teshis veriyordu.

  test("sorunun/cevabin icindeki konu kelimeleri eslesmez", () => {
    const echoed = [
      "--------",
      "user",
      "SORU: JWT refresh token rotasyonu dogru mu?",
      "codex",
      "Cevap: 401 Unauthorized yolunu da dusunmelisin. Not logged in durumu ayri.",
      "tokens used",
    ].join("\n");
    assert.equal(AUTH_FAILURE.test(echoed), false);
  });

  test("codex'in kendi hata satiri eslesir", () => {
    const real =
      "ERROR: Your access token could not be refreshed because your refresh token was already used. Please log out and sign in again.";
    assert.equal(AUTH_FAILURE.test(real), true);
  });

  test("401 tani satiri eslesir", () => {
    assert.equal(
      AUTH_FAILURE.test("Error: HTTP error: 401 Unauthorized, url: wss://chatgpt.com/..."),
      true,
    );
  });
});

describe("SYSTEM_PROMPT", () => {
  test("nezaket onayini yasaklar", () => {
    assert.match(SYSTEM_PROMPT, /Nezaket icin katilma/);
  });

  test("arac kullanimini yasaklar", () => {
    assert.match(SYSTEM_PROMPT, /Komut calistirma/);
  });

  test("makine tarafindan okunan bir KARAR satiri istemez", () => {
    // Kasitli: karari okuyan makine degil, proje sahibi.
    assert.doesNotMatch(SYSTEM_PROMPT, /^KARAR:/m);
  });
});
