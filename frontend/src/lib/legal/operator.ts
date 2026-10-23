/**
 * T168 — the ONE place that holds who runs Skinora.
 *
 * The legal pages (terms, privacy/KVKK notice, support), the MASAK
 * counterparty-declaration lines and the support e-mail fallbacks all read
 * from here. Owner decision (2026-10-21): there is no company yet, so the
 * identity fields stay empty; every text that needs one shows the
 * "company details to be added" marker instead (`legal.operator.pending`),
 * and the DRAFT notice stays on the legal pages until every required field
 * below is filled. Mainnet launch waits for this (11 §F9 T168).
 *
 * To fill in: set the fields, bump {@link LEGAL_TEXT_DATE}, and bump
 * `NEXT_PUBLIC_TOS_VERSION` if the terms changed in substance (the client
 * re-prompts every account on a new version — WP11).
 */
export interface OperatorInfo {
  /** Ticari unvan (company) or full name (sole trader). */
  legalName: string;
  /** Registered address (tebligat adresi). */
  address: string;
  /** Vergi dairesi. */
  taxOffice: string;
  /** Vergi kimlik numarası. */
  taxId: string;
  /** MERSİS numarası — companies only; leave empty for a sole trader. */
  mersisNo: string;
  /** KEP (registered e-mail) address, if any. */
  kepAddress: string;
  /**
   * Yetkili mahkeme ve icra daireleri — the venue named in the governing law
   * clause, e.g. "İstanbul (Merkez)". Filled in together with the company.
   */
  courtVenue: string;
}

export const OPERATOR_INFO: OperatorInfo = {
  legalName: "",
  address: "",
  taxOffice: "",
  taxId: "",
  mersisNo: "",
  kepAddress: "",
  courtVenue: "",
};

/**
 * Fields whose absence keeps the legal texts a DRAFT. `mersisNo` and
 * `kepAddress` are optional (a sole trader has neither).
 */
export const REQUIRED_OPERATOR_FIELDS = [
  "legalName",
  "address",
  "taxOffice",
  "taxId",
  "courtVenue",
] as const satisfies readonly (keyof OperatorInfo)[];

/** Display order of the operator block on the legal pages. */
export const OPERATOR_FIELD_ORDER = [
  "legalName",
  "address",
  "taxOffice",
  "taxId",
  "mersisNo",
  "kepAddress",
  "courtVenue",
] as const satisfies readonly (keyof OperatorInfo)[];

/**
 * Support / data-protection contact. One domain for every address the product
 * shows (owner decision 2026-10-21: `skinora.com`; the earlier
 * `support@skinora.app` fallback was a different domain from the operator
 * alerts' `admin@skinora.com`).
 */
export const SUPPORT_EMAIL = "support@skinora.com";

/** `mailto:` fallback for the "contact support" buttons (overridable by env). */
export const SUPPORT_HREF = process.env.NEXT_PUBLIC_SUPPORT_URL ?? `mailto:${SUPPORT_EMAIL}`;

/** Date shown as "last updated" on the legal pages (ISO, yyyy-mm-dd). */
export const LEGAL_TEXT_DATE = "2026-10-23";

export function isOperatorInfoComplete(info: OperatorInfo = OPERATOR_INFO): boolean {
  return REQUIRED_OPERATOR_FIELDS.every((field) => info[field].trim() !== "");
}

/**
 * ICU values for the legal texts: each operator field, or `pending` (the
 * localized "company details to be added" marker) while it is empty.
 */
export function operatorMessageValues(
  pending: string,
  info: OperatorInfo = OPERATOR_INFO,
): Record<keyof OperatorInfo | "supportEmail", string> {
  const value = (field: keyof OperatorInfo) => {
    const v = info[field].trim();
    return v === "" ? pending : v;
  };
  return {
    legalName: value("legalName"),
    address: value("address"),
    taxOffice: value("taxOffice"),
    taxId: value("taxId"),
    mersisNo: value("mersisNo"),
    kepAddress: value("kepAddress"),
    courtVenue: value("courtVenue"),
    supportEmail: SUPPORT_EMAIL,
  };
}
