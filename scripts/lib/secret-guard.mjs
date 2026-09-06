/**
 * Outbound secret guard — the pre-commit hook's Layer 2 patterns
 * (scripts/git-hooks/pre-commit:125-201), ported to JS for the path that
 * leaves the machine instead of the path that enters git.
 *
 * The two guards protect different edges of the same mistake. The hook stops
 * a secret from being committed; this stops one from being pasted into a
 * question and shipped to a third-party model, where no later `git rm` can
 * recall it.
 *
 * The asymmetry that shapes every rule here: a false positive costs one edit,
 * a false negative is permanent. So this port BLOCKS wherever the hook blocks
 * — including a bare 64-hex string, which was briefly a warning until it was
 * pointed out that the warning prints AFTER the owner has approved the text
 * and stops nothing. A warning is not a gate. Tron transaction hashes are also
 * 64 hex and legitimate here, so the block is escapable on purpose
 * (`--allow-hex`), which turns "notice this" into a decision someone makes.
 */

const PLACEHOLDER =
  /your|replace_in_env|changeme|change_me|example|placeholder|<[^>]+>|xxxx|\.\.\.|e2e|test-|fake-|dummy|todo/i;

const NAMED_KEYS =
  "HOT_WALLET_PRIVATE_KEY|HD_WALLET_MNEMONIC|STEAM_API_KEY|TRON_API_KEY|TRON_API_KEY_SECONDARY|" +
  "JWT_SECRET|WEBHOOK_SECRET|BLOCKCHAIN_WEBHOOK_SECRET|INTERNAL_KEY|STEAM_SIDECAR_INTERNAL_KEY|" +
  "BLOCKCHAIN_SIDECAR_INTERNAL_KEY|MSSQL_SA_PASSWORD|REDIS_PASSWORD|TELEGRAM_BOT_TOKEN|" +
  "GRAFANA_ADMIN_PASSWORD|sharedSecret|identitySecret";

const GENERIC_KEYS =
  "[A-Za-z_]*(?:PASSWORD|PASSWD|SECRET|API_?KEY|ACCESS_?KEY|PRIVATE_?KEY|CLIENT_?SECRET|CONNECTION_?STRING)[A-Za-z_]*";

const SECRET_KEYS = `(?:${NAMED_KEYS}|${GENERIC_KEYS})`;

/**
 * Key ... separator ... value.
 *
 * The filler between key and separator is allowed (up to 40 chars, no
 * separator of its own) because a question is prose, not config: people write
 * `JWT_SECRET (prod ortami): <value>`. The strict "separator immediately after
 * the key" form silently let that shape through.
 */
const ASSIGNMENT = new RegExp(
  `(${SECRET_KEYS})["']?[^:=\\n]{0,40}[:=]\\s*["']?(\\S{8,})`,
  "i",
);

/**
 * Tron/EVM private key shape.
 *
 * The `\b[0-9a-f]{64}\b` this was ported from is defeated by the `0x` prefix —
 * the way every EVM wallet exports a key. `\b` never falls between `x` and a
 * hex digit, so the flagship rule saw nothing. Measured, single variable:
 * `deger: <hex>` blocked, `deger: 0x<hex>` sent.
 *
 * Lookaround rather than `\b`, so the prefix is allowed WITHOUT letting a
 * 64-char window slide along a longer hex run — a 66-hex blob is not a key and
 * must stay quiet.
 */
const HEX64 = /(?<![0-9a-zA-Z_])(?:0x)?([0-9a-f]{64})(?![0-9a-zA-Z_])/i;

/** A secret key name anywhere in a string — used to find one in a table cell. */
const KEY_ANYWHERE = new RegExp(SECRET_KEYS, "i");

/**
 * The hook skips comment-prefixed lines because in a DIFF they are prose
 * describing a key name. A question document is not a diff: `#` is a heading,
 * `>` is a quote of real material, `--` opens a SQL snippet. Pasting a live
 * value under any of those is exactly how a secret would arrive here, so the
 * skip is deliberately NOT ported. False positives are absorbed by the
 * placeholder / ${VAR} / identifier-chain filters below.
 */

/**
 * Scan question text for secrets.
 * @param {string} text
 * @param {{allowHex?: boolean}} [options] allowHex downgrades the bare-64-hex
 *        rule to a warning — for questions that genuinely quote a full Tron
 *        transaction hash.
 * @returns {{blocking: Array<{rule: string, hint: string, line: number}>,
 *            warnings: Array<{rule: string, hint: string, line: number}>}}
 */
export function scanForSecrets(text, options = {}) {
  const blocking = [];
  const warnings = [];
  const lines = text.split(/\r?\n/);

  // 1) PEM private key block — never a false positive worth tolerating.
  lines.forEach((line, i) => {
    if (/BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY/.test(line)) {
      blocking.push({ rule: "PEM private key blogu", hint: "", line: i + 1 });
    }
  });

  // 2) OpenAI-style key. Not in the hook's list (the hook guards this repo's
  //    secrets); added here because the transport itself carries one and a
  //    pasted key would go straight back out to the provider.
  lines.forEach((line, i) => {
    const m = line.match(/\bsk-[A-Za-z0-9_-]{20,}/);
    if (m && !PLACEHOLDER.test(m[0])) {
      blocking.push({
        rule: "OpenAI API anahtari kalibi",
        hint: `${m[0].slice(0, 8)}...`,
        line: i + 1,
      });
    }
  });

  // 3) BIP-39 mnemonic. Matched on WHITESPACE-NORMALISED text, not raw lines:
  //    a seed phrase pasted from a wallet arrives wrapped, numbered, or in a
  //    column, and the single-space single-line form is the one shape it
  //    almost never has.
  const normalised = text
    .replace(/^\s*\d{1,2}[.)]\s*/gm, " ") // "1. word  2. word"
    .replace(/[\s\r\n]+/g, " ");
  const mnemonic = normalised.match(/\b(?:[a-z]{3,8} ){11}[a-z]{3,8}\b/);
  if (mnemonic && !PLACEHOLDER.test(mnemonic[0])) {
    blocking.push({
      rule: "BIP-39 mnemonic kalibi (12/24 kelime)",
      hint: `${mnemonic[0].slice(0, 20)}...`,
      line: lineOf(lines, mnemonic[0].split(" ")[0]),
    });
  }

  // 4) Real value assigned to a known secret key. Comment-prefixed lines are
  //    NOT skipped here — see the note above the function.
  lines.forEach((line, i) => {
    const m = line.match(ASSIGNMENT);
    if (!m) return;
    const value = m[2];
    // Placeholder check on the VALUE, not the whole line: a line reading
    // "prod JWT_SECRET (test-ortaminda farkli): <live value>" contains
    // "test-" and would otherwise wave the real value through.
    if (PLACEHOLDER.test(value)) return;
    if (/\$\{?[A-Za-z_]/.test(value)) return; // ${VAR} reference, not a value
    if (/^(?:get;|set;|string\.|null|true|false|\{)/.test(value)) return;
    // Unquoted identifier chain is code (`bot.sharedSecret = x.shared_secret`),
    // not a literal. A hardcoded secret carries quotes, whose closing quote
    // falls outside this pattern and is still caught.
    if (/^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+[\s;,)}\]|]*$/.test(value)) return;
    blocking.push({
      rule: `sir anahtarina gercek deger: ${m[1]}`,
      hint: `${value.slice(0, 6)}...`,
      line: i + 1,
    });
  });

  // 4b) Markdown table row — `| KEY | value |`.
  //
  // Rule 4 cannot see this: a pipe is not `:` or `=`. That matters more here
  // than anywhere else, because the question template in .claude/skills/gorus.md
  // MANDATES tables ("Baglam", "Elenen alternatifler") — so a table is the one
  // document shape this guard is guaranteed to be handed.
  //
  // The value must FILL its cell: one token, no spaces. A prose cell
  // ("| REDIS_PASSWORD | prod'da ayri tutulur |") documents a key rather than
  // carrying one, and the template is full of exactly that kind of cell.
  lines.forEach((line, i) => {
    if (!/^\s*\|.*\|\s*$/.test(line)) return;
    const cells = line.split("|").slice(1, -1).map((c) => c.trim());
    if (!cells.some((c) => KEY_ANYWHERE.test(c))) return;
    for (const cell of cells) {
      if (KEY_ANYWHERE.test(cell)) continue;
      const value = cell.replace(/^["'`]+|["'`]+$/g, "");
      if (!/^\S{8,}$/.test(value)) continue;
      if (/^[-:]+$/.test(value)) continue; // |---|---| separator row
      if (PLACEHOLDER.test(value)) continue;
      if (/\$\{?[A-Za-z_]/.test(value)) continue;
      if (/^[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+$/.test(value)) continue;
      blocking.push({
        rule: "tablo hucresinde sir degeri",
        hint: `${value.slice(0, 6)}...`,
        line: i + 1,
      });
      break;
    }
  });

  // 5) Secret key and value on SEPARATE lines — the shape a pasted JSON/YAML
  //    block or a markdown table has, which the single-line rule cannot see.
  lines.forEach((line, i) => {
    if (!new RegExp(`${SECRET_KEYS}`, "i").test(line)) return;
    if (ASSIGNMENT.test(line)) return; // already handled above
    const next = lines[i + 1];
    if (!next) return;
    const m = next.match(/^\s*["']?([^\s"',]{16,})["']?\s*,?\s*$/);
    if (!m) return;
    if (PLACEHOLDER.test(m[1])) return;
    if (/\$\{?[A-Za-z_]/.test(m[1])) return;
    blocking.push({
      rule: "sir anahtari, degeri bir sonraki satirda",
      hint: `${m[1].slice(0, 6)}...`,
      line: i + 2,
    });
  });

  // 6) 64-hex, bare or `0x`-prefixed. BLOCKING by default: a Tron/EVM private
  //    key has exactly this shape and a warning printed after approval stops
  //    nothing. Tron tx hashes share the shape and are legitimate, so
  //    --allow-hex downgrades this to a warning — a decision someone makes,
  //    not a notice they miss.
  lines.forEach((line, i) => {
    const m = line.match(HEX64);
    if (!m) return;
    const hex = m[1]; // without any 0x prefix, so the fixture test still holds
    if (/^(.)\1{63}$/.test(hex)) return; // aaaa... fixture
    const finding = {
      rule: options.allowHex
        ? "64-hex dizi (--allow-hex ile gecildi)"
        : "64-hex dizi — private key olabilir (tx hash ise --allow-hex veya kisalt)",
      hint: `${hex.slice(0, 8)}...`,
      line: i + 1,
    };
    (options.allowHex ? warnings : blocking).push(finding);
  });

  return { blocking, warnings };
}

function lineOf(lines, needle) {
  const idx = lines.findIndex((l) => l.includes(needle));
  return idx >= 0 ? idx + 1 : 1;
}
