import { describe, it, expect } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

/**
 * T134 — enum katalog bekçisi.
 *
 * `src/types/enums.ts` is a *copy* of the C# catalogue in
 * `backend/src/Skinora.Shared/Enums` (06 §2). Copies drift: at the start of
 * T134 the frontend still carried `ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_*`,
 * four retired `BOT_*` audit actions and three whole enums the backend had
 * deleted in T117/T132 — and nothing failed, because no test compared the two
 * sides. TypeScript cannot: it only sees this file.
 *
 * This test compares them directly, values AND order (06 §2 is order-normative
 * — 07 §8.1 mirrors it). It reads the C# sources rather than a generated
 * artefact so there is nothing to regenerate and forget.
 *
 * The guard also checks ITSELF: if the parser stops matching (a C# formatting
 * change, a file move), it fails loudly instead of quietly comparing nothing.
 *
 * SCOPE — state it, do not assume it. This covers exactly what `enums.ts`
 * declares as a TS `enum`. Catalogue copies written as string unions elsewhere
 * in the frontend are NOT compared against C# by anything. WP6a closed the one
 * known case: `EmergencyHoldReleaseAction` was a bare string union in
 * `lib/api/admin.ts` and now lives in `enums.ts`, so this guard covers it.
 */

const here = dirname(fileURLToPath(import.meta.url));
const frontendEnumsFile = join(here, "enums.ts");
const backendEnumDir = join(here, "..", "..", "..", "backend", "src", "Skinora.Shared", "Enums");

/** Lower bound on the comparison surface — see "checks ITSELF" above. */
const MIN_SHARED_ENUMS = 20;

function parseFrontendEnums(): Map<string, string[]> {
  const src = readFileSync(frontendEnumsFile, "utf8");
  const out = new Map<string, string[]>();
  for (const m of src.matchAll(/export enum (\w+) \{([\s\S]*?)\n\}/g)) {
    const members = [...m[2].matchAll(/^ {2}([A-Z][A-Z0-9_]*) = "([^"]*)",$/gm)];
    // The TS side is `NAME = "NAME"`; a mismatch would silently change the wire
    // value while the identifier still looks right.
    for (const member of members) {
      expect(member[2], `${m[1]}.${member[1]} must serialise to its own name`).toBe(member[1]);
    }
    out.set(
      m[1],
      members.map((x) => x[1]),
    );
  }
  return out;
}

function parseBackendEnums(): Map<string, string[]> {
  const out = new Map<string, string[]>();
  for (const file of readdirSync(backendEnumDir)) {
    if (!file.endsWith(".cs")) continue;
    const src = readFileSync(join(backendEnumDir, file), "utf8");
    const m = src.match(/public enum (\w+)\s*(?::\s*\w+\s*)?\{([\s\S]*?)\n\}/);
    if (!m) continue;
    out.set(
      m[1],
      [...m[2].matchAll(/^ {4}([A-Z][A-Z0-9_]*)\s*(?:=[^,\n]*)?,?\s*$/gm)].map((x) => x[1]),
    );
  }
  return out;
}

const frontendEnums = parseFrontendEnums();
const backendEnums = parseBackendEnums();
const sharedNames = [...frontendEnums.keys()].filter((n) => backendEnums.has(n));

describe("enum catalogue parity (frontend copy ↔ C# source, 06 §2)", () => {
  it("parsed both sides — the guard is actually comparing something", () => {
    expect(frontendEnums.size).toBeGreaterThanOrEqual(MIN_SHARED_ENUMS);
    expect(backendEnums.size).toBeGreaterThanOrEqual(MIN_SHARED_ENUMS);
    expect(sharedNames.length).toBeGreaterThanOrEqual(MIN_SHARED_ENUMS);
    // A backend enum that parses to zero members means the regex lost the file,
    // which would make every comparison against it vacuously wrong.
    for (const name of sharedNames) {
      expect(backendEnums.get(name), `${name} parsed to no members`).not.toHaveLength(0);
    }
  });

  it("declares no enum the backend does not have", () => {
    // T117/T132 deleted TradeOfferDirection, TradeOfferStatus and
    // PlatformSteamBotStatus; the frontend kept all three until T134.
    const orphans = [...frontendEnums.keys()].filter((n) => !backendEnums.has(n));
    expect(orphans).toEqual([]);
  });

  it.each(sharedNames)("%s matches the C# member list, in order", (name) => {
    expect(frontendEnums.get(name)).toEqual(backendEnums.get(name));
  });
});
