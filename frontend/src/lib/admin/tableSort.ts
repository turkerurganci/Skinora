import type { TableSortOrder } from "@/components/common";

export interface TableSortState {
  by: string | null;
  order: TableSortOrder;
}

/**
 * Parse `sortBy`/`sortOrder` from URL search params (WP13 admin-table-sort),
 * validating `sortBy` against the column's allowed backend keys. Returns the
 * default (no explicit column) when absent or invalid, so the list falls back
 * to the server's default ordering.
 */
export function parseTableSort(
  params: URLSearchParams,
  allowedKeys: readonly string[],
  defaultOrder: TableSortOrder = "desc",
): TableSortState {
  const by = params.get("sortBy");
  const order = params.get("sortOrder");
  const validBy = by && allowedKeys.includes(by) ? by : null;
  const validOrder: TableSortOrder = order === "asc" || order === "desc" ? order : defaultOrder;
  return { by: validBy, order: validBy ? validOrder : defaultOrder };
}

/**
 * Compute the next sort state when a sortable header is clicked: toggle the
 * order if the same column is re-clicked, otherwise switch to the new column at
 * the default order.
 */
export function nextTableSort(
  current: TableSortState,
  sortKey: string,
  defaultOrder: TableSortOrder = "desc",
): TableSortState {
  if (current.by === sortKey) {
    return { by: sortKey, order: current.order === "asc" ? "desc" : "asc" };
  }
  return { by: sortKey, order: defaultOrder };
}
