/**
 * T168 — ordered section ids of the public legal pages. Each id resolves to
 * `legal.<page>.sections.<id>.{heading,body}` in every locale (checked by
 * `components/legal/legal.test.tsx`). Kept outside the `page.tsx` files
 * because an App Router page may only export its component and route config.
 */
export const TERMS_SECTIONS = [
  "parties",
  "eligibility",
  "serviceDescription",
  "transactionFlow",
  "paymentAndNetworks",
  "fees",
  "delivery",
  "settlement",
  "cancellation",
  "refunds",
  "disputes",
  "userObligations",
  "visibility",
  "restrictions",
  "masak",
  "liability",
  "indemnity",
  "suspension",
  "changes",
  "governingLaw",
  "contact",
] as const;

export const PRIVACY_SECTIONS = [
  "controller",
  "dataWeCollect",
  "collectionMethod",
  "purposes",
  "dataSharing",
  "blockchainPublic",
  "dataRetention",
  "yourRights",
  "security",
  "cookies",
  "minors",
  "changes",
  "contact",
] as const;

// T167 — "masak" (anchor `#masak`): what Turkish exchanges do to payments and
// payouts under MASAK Tebliğ 29 / Tedbirler Yönetmeliği m. 24/A; the payment
// screens link here.
export const SUPPORT_SECTIONS = ["contact", "responseTimes", "disputes", "masak", "faq"] as const;
