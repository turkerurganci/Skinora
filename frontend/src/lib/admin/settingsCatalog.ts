import type { AdminSettingItem } from "@/lib/api/admin";

/**
 * Client-side presentation metadata for the S17 settings screen (04 §8.6).
 *
 * The AD8 contract (07 §9.8) returns a flat list of settings tagged with a
 * lowercase `category`, but it carries neither a UI group title nor an
 * impact-scope field. This module supplies both, derived from the category:
 *
 *  - {@link SETTING_GROUPS} folds the 15 backend categories into the 04 §8.6
 *    admin parameter groups (e.g. `geo_blocking` + `age_verification` →
 *    "Erişim ve Uyumluluk"). Three categories the spec never documented
 *    (`reputation`, `platform_maintenance`, `retention`) are real operational
 *    settings, so they render under a separate "operational" section rather
 *    than being hidden (T102 owner decision).
 *  - {@link impactForCategory} maps each category to its 04 §8.6 impact class.
 *
 * Group titles / impact labels are localised in the `adminSettings` i18n
 * namespace; this module only owns the structural classification.
 */

/** 04 §8.6 impact-scope classes shown in the info box + per-row label. */
export type SettingImpact = "newTransaction" | "runtime" | "supportingSignal";

/**
 * Categories whose changes take effect on active sessions / transactions
 * immediately rather than only on newly created transactions (04 §8.6):
 *  - `geo_blocking` / `age_verification` gate new sessions,
 *  - `blockchain_health` freezes the payment step on a failed health check,
 *  - `platform_maintenance` flips the platform-wide maintenance banner live.
 * Everything else is "new transaction only".
 */
const RUNTIME_CATEGORIES: ReadonlySet<string> = new Set([
  "geo_blocking",
  "age_verification",
  "blockchain_health",
  "platform_maintenance",
]);

/**
 * Resolve the impact class for a category. No category currently maps to
 * `supportingSignal` — VPN keys (the only supporting-signal parameters in
 * 04 §8.6) are not exposed by the catalog — but the class is documented in the
 * info box so the three-way 04 §8.6 distinction stays visible.
 */
export function impactForCategory(category: string): SettingImpact {
  return RUNTIME_CATEGORIES.has(category) ? "runtime" : "newTransaction";
}

export type SettingGroupSection = "documented" | "operational";

interface SettingGroupDef {
  /** Key under the `adminSettings.groups` i18n namespace. */
  key: string;
  /** Backend categories folded into this UI group. */
  categories: readonly string[];
  section: SettingGroupSection;
}

/**
 * 04 §8.6 admin parameter groups, in spec order, followed by the operational
 * groups the spec omits. A category may belong to exactly one group.
 */
const SETTING_GROUPS: readonly SettingGroupDef[] = [
  { key: "timeout", categories: ["timeout"], section: "documented" },
  { key: "commission", categories: ["commission"], section: "documented" },
  { key: "transactionLimits", categories: ["transaction_limits"], section: "documented" },
  { key: "cancelRules", categories: ["cancel_rules"], section: "documented" },
  { key: "newAccount", categories: ["new_account"], section: "documented" },
  { key: "gasFee", categories: ["gas_fee"], section: "documented" },
  { key: "fraudDetection", categories: ["fraud_detection"], section: "documented" },
  { key: "buyerIdentification", categories: ["buyer_identification"], section: "documented" },
  {
    key: "accessCompliance",
    categories: ["geo_blocking", "age_verification"],
    section: "documented",
  },
  { key: "blockchainHealth", categories: ["blockchain_health"], section: "documented" },
  { key: "wallet", categories: ["wallet_security"], section: "documented" },
  { key: "reputation", categories: ["reputation"], section: "operational" },
  { key: "platformMaintenance", categories: ["platform_maintenance"], section: "operational" },
  { key: "retention", categories: ["retention"], section: "operational" },
];

/** A rendered group: its i18n key, section, and the settings that belong to it. */
export interface SettingGroup {
  key: string;
  section: SettingGroupSection;
  settings: AdminSettingItem[];
}

/**
 * Fold a flat AD8 settings list into ordered UI groups, preserving the backend
 * (= catalog) order within each group. Settings whose category is not claimed
 * by any known group are collected into a trailing `other` operational group so
 * a future backend category is surfaced rather than silently dropped.
 */
export function groupSettings(settings: readonly AdminSettingItem[]): SettingGroup[] {
  const claimed = new Set<string>();
  const groups: SettingGroup[] = [];

  for (const def of SETTING_GROUPS) {
    const inGroup = settings.filter((s) => def.categories.includes(s.category));
    if (inGroup.length === 0) continue;
    def.categories.forEach((c) => claimed.add(c));
    groups.push({ key: def.key, section: def.section, settings: inGroup });
  }

  const leftover = settings.filter((s) => !claimed.has(s.category));
  if (leftover.length > 0) {
    groups.push({ key: "other", section: "operational", settings: leftover });
  }

  return groups;
}
