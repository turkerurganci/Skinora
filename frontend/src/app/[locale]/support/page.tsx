import { LegalPage } from "@/components/legal";

const SECTIONS = ["contact", "responseTimes", "disputes", "faq"] as const;

export default function SupportPage() {
  return <LegalPage namespace="legal.support" sectionKeys={SECTIONS} />;
}
