"use client";

import { useQuery } from "@tanstack/react-query";
import { getEligibility } from "@/lib/api/transactions";

/**
 * S06 form-pre eligibility gate hook (07 §7.3, 04 §7.2 "Form Öncesi Engeller").
 *
 * `enabled` is wired to the page-level auth check — calling the endpoint
 * anonymously would always 401 and inflate the error surface. The result is
 * not cached aggressively (no `staleTime`) because the engel state'leri can
 * shift mid-session (admin lifts cooldown, MA gets verified) and we want a
 * fresh read on every form mount.
 */
export function useEligibility(enabled = true) {
  return useQuery({
    queryKey: ["transactions", "eligibility"],
    queryFn: getEligibility,
    enabled,
  });
}
