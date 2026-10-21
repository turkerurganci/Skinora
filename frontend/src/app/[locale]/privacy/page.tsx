import { LegalPage } from "@/components/legal";
import { PRIVACY_SECTIONS } from "@/lib/legal/sections";

export default function PrivacyPage() {
  return <LegalPage namespace="legal.privacy" sectionKeys={PRIVACY_SECTIONS} showContents />;
}
