"use client";

import { Fragment, type ReactNode } from "react";
import { cn } from "@/lib/utils/cn";

export type TableSortOrder = "asc" | "desc";

/**
 * Optional click-to-sort wiring for the desktop header (WP13 admin-table-sort).
 * `by` is the active column's {@link ResponsiveTableColumn.sortKey} (or null
 * when the list uses its default server order); `onSort` is called with a
 * column's sortKey when its header is clicked — the caller decides the toggle.
 */
export interface ResponsiveTableSort {
  by: string | null;
  order: TableSortOrder;
  onSort: (sortKey: string) => void;
}

export interface ResponsiveTableColumn<T> {
  key: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  headerClassName?: string;
  cellClassName?: string;
  /**
   * If true, the column is omitted from the mobile card list.
   * Use for low-value columns that already appear in the primary header line.
   */
  mobileHidden?: boolean;
  /**
   * Backend sort key for this column. When set and the table receives a
   * {@link ResponsiveTableProps.sort} handler, the desktop header becomes a
   * sort toggle button. Omit for non-sortable columns.
   */
  sortKey?: string;
}

export interface ResponsiveTableProps<T> {
  data: readonly T[];
  columns: ReadonlyArray<ResponsiveTableColumn<T>>;
  getRowKey: (row: T) => string;
  ariaLabel: string;
  emptyMessage?: ReactNode;
  className?: string;
  /**
   * Override the entire mobile card body for a row. When supplied, the default
   * label/value list rendering is skipped (column headers are still consulted
   * for the desktop <table> view).
   */
  mobileRender?: (row: T) => ReactNode;
  /** Enables desktop click-to-sort on columns that declare a `sortKey`. */
  sort?: ResponsiveTableSort;
}

/**
 * Renders a list of records as a semantic `<table>` on desktop / tablet
 * (>= md, 768px) and as a stack of cards on mobile (< md). Each card uses
 * column headers as field labels — this implements 04 §9.4 "Tablo → Kart
 * Dönüşümü". Pass `mobileRender` to fully customize the mobile body.
 */
export function ResponsiveTable<T>({
  data,
  columns,
  getRowKey,
  ariaLabel,
  emptyMessage,
  className,
  mobileRender,
  sort,
}: ResponsiveTableProps<T>) {
  if (data.length === 0 && emptyMessage !== undefined) {
    return (
      <div
        className={cn(
          "rounded-lg border border-gray-200 bg-white p-6 text-center text-sm text-gray-500",
          className,
        )}
      >
        {emptyMessage}
      </div>
    );
  }

  const mobileColumns = columns.filter((c) => !c.mobileHidden);

  return (
    <div className={className}>
      <div className="hidden md:block">
        <table
          className="w-full table-auto border-collapse text-left text-sm"
          aria-label={ariaLabel}
        >
          <thead>
            <tr className="border-b border-gray-200 bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-500">
              {columns.map((col) => {
                const sortable = !!sort && !!col.sortKey;
                const active = sortable && sort!.by === col.sortKey;
                return (
                  <th
                    key={col.key}
                    scope="col"
                    aria-sort={
                      active ? (sort!.order === "asc" ? "ascending" : "descending") : undefined
                    }
                    className={cn("px-3 py-2 align-middle", col.headerClassName)}
                  >
                    {sortable ? (
                      <button
                        type="button"
                        onClick={() => sort!.onSort(col.sortKey!)}
                        className="inline-flex items-center gap-1 uppercase tracking-wide hover:text-gray-900"
                      >
                        {col.header}
                        <span
                          aria-hidden="true"
                          className={cn(active ? "text-gray-900" : "text-gray-400")}
                        >
                          {active ? (sort!.order === "asc" ? "▲" : "▼") : "↕"}
                        </span>
                      </button>
                    ) : (
                      col.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {data.map((row) => (
              <tr key={getRowKey(row)} className="border-b border-gray-100 last:border-b-0">
                {columns.map((col) => (
                  <td
                    key={col.key}
                    className={cn("px-3 py-2 align-middle text-gray-900", col.cellClassName)}
                  >
                    {col.cell(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ul role="list" aria-label={ariaLabel} className="flex flex-col gap-3 md:hidden">
        {data.map((row) => (
          <li
            key={getRowKey(row)}
            className="rounded-lg border border-gray-200 bg-white p-3 shadow-sm"
          >
            {mobileRender ? (
              mobileRender(row)
            ) : (
              <dl className="flex flex-col gap-1">
                {mobileColumns.map((col, idx) => (
                  <Fragment key={col.key}>
                    <div
                      className={cn(
                        "flex items-start justify-between gap-3 text-sm",
                        idx === 0 && "font-semibold text-gray-900",
                      )}
                    >
                      <dt className="text-xs font-medium uppercase tracking-wide text-gray-500">
                        {col.header}
                      </dt>
                      <dd className="text-right text-gray-900">{col.cell(row)}</dd>
                    </div>
                  </Fragment>
                ))}
              </dl>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
