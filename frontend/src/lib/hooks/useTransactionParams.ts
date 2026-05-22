"use client";

import { useQuery } from "@tanstack/react-query";
import { getTransactionParams } from "@/lib/api/transactions";

/**
 * S06 form parameters hook (07 §7.4).
 *
 * These values are admin-tunable but change rarely, so we mark the query
 * `staleTime: Infinity` — a refetch only happens on form remount. Keeping the
 * shape stable across the form's 4 steps avoids mid-flow validation drift
 * (e.g. admin updates minPrice while the user is in step 3).
 */
export function useTransactionParams(enabled = true) {
  return useQuery({
    queryKey: ["transactions", "params"],
    queryFn: getTransactionParams,
    enabled,
    staleTime: Infinity,
  });
}
