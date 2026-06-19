import { LegalPage } from "@/components/legal";

const SECTIONS = [
  "dataWeCollect",
  "howWeUse",
  "dataSharing",
  "dataRetention",
  "yourRights",
  "contact",
] as const;

export default function PrivacyPage() {
  return <LegalPage namespace="legal.privacy" sectionKeys={SECTIONS} />;
}
