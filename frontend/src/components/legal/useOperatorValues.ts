"use client";

import { useTranslations } from "next-intl";
import { operatorMessageValues } from "@/lib/legal/operator";

/**
 * T168 — ICU values for any text that names the operator ({legalName},
 * {address}, {courtVenue}, {supportEmail}, …). Empty fields render as the
 * localized "company details to be added" marker, so a text never shows a
 * blank where the operator's identity belongs.
 */
export function useOperatorValues() {
  const t = useTranslations("legal.operator");
  return operatorMessageValues(t("pending"));
}
