#!/usr/bin/env node
// WP18 — i18n parity + untranslatable-term lint (no dependencies, runs under Node 20+).
//
// Two checks, both required to keep the four locale message files honest:
//   1. KEY PARITY (BLOCKING) — en/tr/es/zh must have identical flattened key
//      sets (no missing, no extra). Today this is held only by manual discipline
//      (validators hand-counted "Nx4 IDENTICAL"); this gate makes it enforced.
//   2. UNTRANSLATABLE TERMS (ADVISORY) — every term in UNTRANSLATABLE_TERMS
//      (04 §10.4, single source = src/i18n/untranslatable.ts) that appears in an
//      en value should appear verbatim (case-sensitive) in the same key of every
//      other locale, catching accidental translations (e.g. "Steam ID" ->
//      "Steam Kimliği"). Reported as warnings only — it does NOT fail the build.
//      Owner decision (WP18): the current "Gas fee"/"Mobile Authenticator"
//      localizations are a spec-vs-translation content question left to a
//      follow-up (see DEFERRED_BACKLOG), so this rule stays advisory for now.
//
// Exit code: non-zero ONLY on a parity violation. Untranslatable warnings are
// printed for visibility but never fail CI. Run in the CI lint job and locally
// via `npm run i18n:check`.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const frontendRoot = join(here, "..");
const msgDir = join(frontendRoot, "src", "i18n", "messages");
const LOCALES = ["en", "tr", "es", "zh"];
const BASE = "en";

// --- load locale files ---
const data = {};
for (const loc of LOCALES) {
  data[loc] = JSON.parse(readFileSync(join(msgDir, `${loc}.json`), "utf8"));
}

// --- flatten nested objects to dotted leaf paths ---
function flatten(obj, prefix = "", out = new Map()) {
  for (const [k, v] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object" && !Array.isArray(v)) {
      flatten(v, path, out);
    } else {
      out.set(path, v);
    }
  }
  return out;
}
const flat = {};
for (const loc of LOCALES) flat[loc] = flatten(data[loc]);

const parityErrors = []; // fatal — fail the build
const untranslatableWarnings = []; // advisory — reported, never fatal

// --- check 1: key parity (BLOCKING) ---
const baseKeys = new Set(flat[BASE].keys());
for (const loc of LOCALES) {
  if (loc === BASE) continue;
  const keys = new Set(flat[loc].keys());
  const missing = [...baseKeys].filter((k) => !keys.has(k));
  const extra = [...keys].filter((k) => !baseKeys.has(k));
  if (missing.length) {
    parityErrors.push(
      `[parity] ${loc}.json is missing ${missing.length} key(s) present in ${BASE}.json: ` +
        `${missing.slice(0, 15).join(", ")}${missing.length > 15 ? " …" : ""}`,
    );
  }
  if (extra.length) {
    parityErrors.push(
      `[parity] ${loc}.json has ${extra.length} extra key(s) not in ${BASE}.json: ` +
        `${extra.slice(0, 15).join(", ")}${extra.length > 15 ? " …" : ""}`,
    );
  }
}

// --- check 2: untranslatable terms (ADVISORY; single source = untranslatable.ts) ---
const tsSrc = readFileSync(join(frontendRoot, "src", "i18n", "untranslatable.ts"), "utf8");
const block = tsSrc.match(/UNTRANSLATABLE_TERMS\s*=\s*\[([\s\S]*?)\]\s*as const/);
if (!block) {
  console.error(
    "check-i18n: could not locate UNTRANSLATABLE_TERMS array in src/i18n/untranslatable.ts — refusing to pass silently.",
  );
  process.exit(1);
}
const terms = [...block[1].matchAll(/"([^"]+)"/g)].map((m) => m[1]);
if (terms.length === 0) {
  console.error("check-i18n: parsed 0 untranslatable terms — refusing to pass silently.");
  process.exit(1);
}

let untranslatableChecks = 0;
for (const [key, baseVal] of flat[BASE]) {
  if (typeof baseVal !== "string") continue;
  for (const term of terms) {
    if (!baseVal.includes(term)) continue;
    for (const loc of LOCALES) {
      if (loc === BASE) continue;
      const locVal = flat[loc].get(key);
      if (typeof locVal !== "string") continue; // missing key already reported by parity
      untranslatableChecks++;
      if (!locVal.includes(term)) {
        untranslatableWarnings.push(
          `[untranslatable] ${loc}.json key "${key}" should keep "${term}" verbatim (04 §10.4) ` +
            `but it is absent. en="${baseVal}" ${loc}="${locVal}"`,
        );
      }
    }
  }
}

// --- report ---
// Advisory block first (never affects exit code).
if (untranslatableWarnings.length > 0) {
  console.warn(
    `i18n untranslatable ADVISORY — ${untranslatableWarnings.length} term(s) localized against 04 §10.4 ` +
      `(non-blocking, see DEFERRED_BACKLOG):`,
  );
  for (const w of untranslatableWarnings) console.warn("  ! " + w);
  console.warn("");
}

// Blocking block: parity is the only thing that fails CI.
if (parityErrors.length > 0) {
  console.error(`i18n parity check FAILED — ${parityErrors.length} issue(s):\n`);
  for (const e of parityErrors) console.error("  - " + e);
  process.exit(1);
}

console.log(
  `i18n parity OK — ${LOCALES.length} locales, ${baseKeys.size} keys each, identical key sets. ` +
    `Untranslatable: ${terms.length} terms checked across ${untranslatableChecks} occurrence(s), ` +
    `${untranslatableWarnings.length} advisory warning(s).`,
);
