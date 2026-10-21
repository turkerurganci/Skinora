import { LegalPage } from "@/components/legal";
import { TERMS_SECTIONS } from "@/lib/legal/sections";

export default function TermsPage() {
  return <LegalPage namespace="legal.terms" sectionKeys={TERMS_SECTIONS} showContents />;
}
