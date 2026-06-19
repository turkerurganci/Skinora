import { LegalPage } from "@/components/legal";

const SECTIONS = [
  "acceptance",
  "serviceDescription",
  "userObligations",
  "fees",
  "liability",
  "termination",
  "contact",
] as const;

export default function TermsPage() {
  return <LegalPage namespace="legal.terms" sectionKeys={SECTIONS} />;
}
