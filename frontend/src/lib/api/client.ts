import type { ApiResponse, ApiErrorDetail } from "@/types/api";

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
 * Returns the stored access token (client-side only).
 * Token storage will be implemented in T29 (auth).
 */
function getAccessToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem("access_token");
}

/**
 * Fetch wrapper that unwraps the ApiResponse<T> envelope.
 * Throws ApiError on non-success responses.
 */
export async function apiClient<T>(
  url: string,
  options?: RequestInit,
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
  });

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
