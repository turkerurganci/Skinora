"use client";

import { useAuthStore } from "@/lib/stores/auth-store";

/**
 * Hook for accessing auth state. Wraps the Zustand store.
 * Full implementation in T29 (auth).
 */
export function useAuth() {
  return useAuthStore();
}
