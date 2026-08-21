import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { KNOWN_PERMISSION_KEYS, permissionLabelKey } from "./permissionCatalog";

/**
 * T136 — admin permission catalogue guard.
 *
 * `KNOWN_PERMISSION_KEYS` is a *copy* of the C# `PermissionCatalog` in
 * `backend/src/Modules/Skinora.Admin/Application/Permissions/PermissionCatalog.cs`
 * (07 §9.11 / 04 §8.8), and the `adminRoles.permissions` i18n block is a second
 * copy of the same list. Both drifted: T132 deleted `VIEW_STEAM_ACCOUNTS` and
 * `MANAGE_STEAM_RECOVERY` backend-side and the frontend kept carrying 14 keys —
 * plus four locales of dead labels — until T136. Nothing failed, because
 * nothing compared the sides. `check-i18n.mjs` cannot: it only proves the four
 * locales agree with *each other*, and all four were wrong in the same way.
 * `enums.parity.test.ts` cannot either: `PermissionCatalog` is a static class
 * with an `IReadOnlyList<PermissionEntry>`, not a C# `enum`, so that parser
 * never sees it.
 *
 * This test compares all three copies directly — keys AND order (07 §9.11 is
 * order-normative: "Frontend renders the S19 yetki matrix in the order returned
 * here"). It reads the C# source rather than a generated artefact so there is
 * nothing to regenerate and forget.
 *
 * The guard also checks ITSELF: if either parser stops matching (a C#
 * formatting change, a file move), it fails loudly instead of quietly comparing
 * empty lists.
 */

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, "..", "..", "..", "..");
const backendCatalogFile = join(
  repoRoot,
  "backend",
  "src",
  "Modules",
  "Skinora.Admin",
  "Application",
  "Permissions",
  "PermissionCatalog.cs",
);
const messagesDir = join(here, "..", "..", "i18n", "messages");
const LOCALES = ["en", "tr", "es", "zh"] as const;

/** Lower bound on the comparison surface — see "checks ITSELF" above. */
const MIN_PERMISSIONS = 10;

/**
 * Parses the C# catalogue in two steps, because `All` refers to the keys
 * symbolically: `Keys.ViewFlags` → `"VIEW_FLAGS"`. Reading only the `Keys`
 * class would lose the order; reading only `All` would lose the wire values.
 */
function parseBackendPermissionKeys(): string[] {
  const src = readFileSync(backendCatalogFile, "utf8");

  const constants = new Map<string, string>();
  for (const m of src.matchAll(/public const string (\w+) = "([A-Z][A-Z0-9_]*)";/g)) {
    constants.set(m[1], m[2]);
  }

  const all = src.match(/All \{ get; \} =\s*\[([\s\S]*?)\n {4}\];/);
  if (!all) return [];

  return [...all[1].matchAll(/new\(Keys\.(\w+),/g)].map((m) => {
    const value = constants.get(m[1]);
    expect(
      value,
      `PermissionCatalog.Keys.${m[1]} is referenced by All but not declared`,
    ).toBeDefined();
    return value as string;
  });
}

function localeLabelKeys(locale: string): string[] {
  const raw = readFileSync(join(messagesDir, `${locale}.json`), "utf8");
  const messages = JSON.parse(raw) as Record<string, unknown>;
  const adminRoles = messages.adminRoles as Record<string, unknown> | undefined;
  const permissions = adminRoles?.permissions as Record<string, string> | undefined;
  return Object.keys(permissions ?? {});
}

const backendKeys = parseBackendPermissionKeys();

describe("admin permission catalogue parity (frontend copies ↔ C# source, 07 §9.11)", () => {
  it("parsed the C# catalogue — the guard is actually comparing something", () => {
    expect(backendKeys.length).toBeGreaterThanOrEqual(MIN_PERMISSIONS);
    // A duplicate would make a length comparison pass while the sets differ.
    expect(new Set(backendKeys).size).toBe(backendKeys.length);
  });

  it("KNOWN_PERMISSION_KEYS matches the C# list, in order", () => {
    expect([...KNOWN_PERMISSION_KEYS]).toEqual(backendKeys);
  });

  it.each(LOCALES)("%s adminRoles.permissions covers exactly the catalogue", (locale) => {
    const labelled = localeLabelKeys(locale);
    // Order is not normative for the i18n block — the S19 matrix maps over the
    // server list, and each label is looked up by key. Membership is.
    expect([...labelled].sort()).toEqual([...backendKeys].sort());
  });

  it("every catalogue key resolves to a label key the locales actually carry", () => {
    // Closes the loop between the two copies: permissionLabelKey() is the only
    // bridge from a catalogue key to an i18n entry, and a dead key here is
    // exactly how the S19 fallback silently pointed at nothing before T136.
    const enLabels = new Set(localeLabelKeys("en"));
    for (const key of KNOWN_PERMISSION_KEYS) {
      expect(permissionLabelKey(key), `${key} label key must be namespaced`).toBe(
        `permissions.${key}`,
      );
      expect(enLabels.has(key), `${key} has no adminRoles.permissions label`).toBe(true);
    }
  });
});
