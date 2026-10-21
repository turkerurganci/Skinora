import { LegalPage } from "@/components/legal";
import { MasakHelpSection } from "@/components/masak";

// T167 — "masak" (anchor `#masak`): what Turkish exchanges do to payments and
// payouts under MASAK Tebliğ 29 / Tedbirler Yönetmeliği m. 24/A; the payment
// screens link here.
const SECTIONS = ["contact", "responseTimes", "disputes", "masak", "faq"] as const;

export default function SupportPage() {
  return (
    <LegalPage
      namespace="legal.support"
      sectionKeys={SECTIONS}
      sectionExtras={{ masak: <MasakHelpSection /> }}
    />
  );
}
