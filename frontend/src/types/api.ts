/**
 * Standard API response envelope (07 §2.4).
 */
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: ApiErrorDetail | null;
  traceId: string;
}

export interface ApiErrorDetail {
  code: string;
  message: string;
  details: Record<string, string[]> | null;
}

/**
 * Paginated result wrapper.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
