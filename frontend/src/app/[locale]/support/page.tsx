import { LegalPage } from "@/components/legal";
import { MasakHelpSection } from "@/components/masak";
import { SUPPORT_SECTIONS } from "@/lib/legal/sections";

export default function SupportPage() {
  return (
    <LegalPage
      namespace="legal.support"
      sectionKeys={SUPPORT_SECTIONS}
      sectionExtras={{ masak: <MasakHelpSection /> }}
    />
  );
}
