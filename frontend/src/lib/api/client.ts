import type { ApiResponse, ApiErrorDetail } from "@/types/api";
import { useAuthStore } from "@/lib/stores/auth-store";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "/api/v1";

/**
 * Structured API error thrown when the backend returns success: false.
 */
export class ApiError extends Error {
  constructor(
    public readonly error: ApiErrorDetail,
    public readonly traceId: string,
    public readonly status: number,
  ) {
    super(error.message);
    this.name = "ApiError";
  }

  /** Validation field errors (if any). */
  get details(): Record<string, string[]> | null {
    return this.error.details;
  }

  get code(): string {
    return this.error.code;
  }
}

/**
 * Returns the stored access token (client-side only). The auth store is the
 * single writer of this key (WP11 — see auth-store.ts).
 */
function getAccessToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem("access_token");
}

/**
 * In-flight refresh promise — concurrent 401s share a single rotation so we
 * don't hammer /auth/refresh or trip the refresh-token reuse detector, which
 * mass-revokes on a replayed token (05 §6.1).
 */
let refreshInFlight: Promise<string | null> | null = null;

/**
 * WP11 — rotates the HttpOnly refresh cookie for a fresh access token
 * (A9, 07 §4.10). Anonymous + cookie-based, so this is a raw credentialed fetch
 * that must NOT route through {@link apiClient} (which would recurse on its own
 * 401 handling). Returns the new access token, or null when the session cannot
 * be refreshed (missing/expired/reused cookie).
 */
export function refreshAccessToken(): Promise<string | null> {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
          method: "POST",
          credentials: "include",
        });
        if (!response.ok) return null;
        const body = (await response.json()) as ApiResponse<{
          accessToken: string;
          expiresIn: number;
        }>;
        if (!body.success || !body.data?.accessToken) return null;
        return body.data.accessToken;
      } catch {
        return null;
      } finally {
        refreshInFlight = null;
      }
    })();
  }
  return refreshInFlight;
}

function redirectToLogin(): void {
  if (typeof window === "undefined") return;
  // Avoid a redirect loop when a page in the auth flow itself returns 401.
  if (window.location.pathname.includes("/auth/")) return;
  window.location.assign("/auth/login");
}

/**
 * Fetch wrapper that unwraps the ApiResponse<T> envelope.
 * Throws ApiError on non-success responses.
 *
 * WP11 — sends credentials (so the refresh cookie flows on same-site requests)
 * and, on a 401, rotates the refresh cookie once and retries the original
 * request. If the refresh fails the session is cleared and the browser is sent
 * to the login page. `isRetry` is internal (recursion guard) — callers pass two
 * args.
 */
export async function apiClient<T>(
  url: string,
  options?: RequestInit,
  isRetry = false,
): Promise<T> {
  const token = getAccessToken();

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options?.headers as Record<string, string>),
  };

  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(`${API_BASE_URL}${url}`, {
    ...options,
    headers,
    credentials: "include",
  });

  // 401 → rotate the refresh cookie once, then retry. The refresh endpoint
  // itself and already-retried requests are excluded to prevent recursion.
  if (response.status === 401 && !isRetry && url !== "/auth/refresh") {
    const newToken = await refreshAccessToken();
    if (newToken) {
      useAuthStore.getState().setAccessToken(newToken);
      return apiClient<T>(url, options, true);
    }
    // Refresh failed → session is over; clear it and bounce to login.
    useAuthStore.getState().logout();
    redirectToLogin();
  }

  const body: ApiResponse<T> = await response.json();

  if (!body.success || body.error) {
    throw new ApiError(
      body.error ?? { code: "UNKNOWN", message: "Unknown error", details: null },
      body.traceId,
      response.status,
    );
  }

  return body.data as T;
}
