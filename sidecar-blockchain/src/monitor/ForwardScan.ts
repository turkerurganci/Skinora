import { logger } from '../logger.js';
import type { ListTrc20Options, Trc20ListResponse, Trc20Record } from '../tron/TronGridClient.js';

/**
 * Where one monitoring scan (an address × a phase) resumes on its next poll
 * (08 §3.4, backlog `MonitorCursorPagesBackward`).
 *
 * <para>
 * Until 2026-10-03 the monitors stored TronGrid's <c>meta.fingerprint</c>
 * and sent it back on the next poll, as if it meant "after what I saw last".
 * It does not: it is the next page of the same query in the query's order,
 * and TronGrid's default order is newest first, so it led to OLDER records
 * (Nile probe 2026-10-02). Once a phase held more records than one page, the
 * monitor stored a cursor, slid back through old pages, and — the last page
 * carrying no cursor — kept asking for that last old page: new transfers in
 * that phase were never seen again.
 * </para>
 *
 * <para>
 * The watermark is the <c>block_timestamp</c> of the newest record handled so
 * far. A poll asks for records from that moment on, oldest first, and reads
 * to the last page. TronGrid's <c>min_timestamp</c> is inclusive and floored
 * to the second (Nile probe 2026-10-03), so the watermark's own block is
 * read again — per-event dedup in the registries absorbs it, and no record
 * from that block can be skipped.
 * </para>
 */
export interface ForwardCursor {
  /** Undefined until the first poll completes a page: start from the address's oldest record. */
  minTimestamp?: number;
  /**
   * Set only when a poll stopped at its page cap while every record it read
   * still sat in the watermark's own second — the watermark could not move,
   * so the next poll continues the same query from this page instead.
   */
  fingerprint?: string;
}

/**
 * Pages one poll reads before it stops and leaves the rest to the next poll.
 * A normal poll reads one page; this only bites while catching up — a
 * restarted sidecar re-reading a long history, or a spam flood. It keeps one
 * address from holding the shared tick (and the TronGrid budget) for a long
 * run of pages.
 */
export const MAX_PAGES_PER_POLL = 5;

export interface ForwardScanArgs {
  client: { listTrc20(options: ListTrc20Options): Promise<Trc20ListResponse> };
  address: string;
  /** Phase 1 filters by the expected contract; phase 2 leaves it unset. */
  contractAddress?: string;
  pageLimit: number;
  /** Advanced in place, and only after every record of a page was handled. */
  cursor: ForwardCursor;
  /** Called for each record, oldest first. A throw aborts the scan; the page is read again next poll. */
  handle: (record: Trc20Record) => Promise<void>;
  maxPages?: number;
}

/**
 * Read the records that arrived at <c>address</c> since the cursor, oldest
 * first, and hand each to <c>handle</c>. The cursor only moves past a page
 * once all of that page's records were handled, so a webhook failure halfway
 * through leaves them to be read (and deduplicated) again on the next poll.
 */
export async function scanForward(args: ForwardScanArgs): Promise<void> {
  const { client, address, contractAddress, pageLimit, cursor, handle } = args;
  const maxPages = args.maxPages ?? MAX_PAGES_PER_POLL;
  const start = cursor.minTimestamp;
  let fingerprint = cursor.fingerprint;
  let newest = start;

  for (let page = 0; page < maxPages; page += 1) {
    const response = await client.listTrc20({
      address,
      contractAddress,
      minTimestamp: start,
      fingerprint,
      limit: pageLimit,
      order: 'asc',
    });
    for (const record of response.records) {
      await handle(record);
    }
    for (const record of response.records) {
      if (
        Number.isFinite(record.block_timestamp) &&
        (newest === undefined || record.block_timestamp > newest)
      ) {
        newest = record.block_timestamp;
      }
    }

    const hasMore = !!response.fingerprint && response.records.length >= pageLimit;
    if (!hasMore) {
      cursor.minTimestamp = newest;
      cursor.fingerprint = undefined;
      return;
    }
    fingerprint = response.fingerprint ?? undefined;
    if (newest !== start) {
      // Progress is kept even if a later page fails: the next poll starts
      // from this page's newest second, re-reading only that second.
      cursor.minTimestamp = newest;
      cursor.fingerprint = undefined;
    } else {
      cursor.fingerprint = fingerprint;
    }
  }

  logger.warn(
    { address, contractAddress, pages: maxPages, watermark: cursor.minTimestamp },
    'Monitor scan hit its page cap — the rest is read on the next poll',
  );
}

/**
 * Pick the on-chain event index for a list record from the transfer logs of
 * its transaction (08 §3.4 — WP10). The record is matched to a log by value;
 * the first matching log not yet reported wins, so two equal transfers in one
 * transaction get two indices.
 *
 * <para>
 * When every matching log was already reported the record is one of those
 * again — the watermark block re-read, or phase 2 meeting a phase 1 transfer
 * — and the reported index is returned so the caller skips it. Returning 0
 * there, as before 2026-10-03, reported a transfer whose real index was not 0
 * a second time under index 0, and the backend recorded a second payment.
 * </para>
 *
 * <para>
 * Index 0 remains the fallback when no log matches at all (the solidity node
 * has no logs for the transaction yet): the single-transfer case, unchanged.
 * </para>
 */
export function pickEventIndex(
  entries: ReadonlyArray<{ index: number; value: string }>,
  value: string,
  isReported: (index: number) => boolean,
): number {
  let reported: number | undefined;
  for (const entry of entries) {
    if (entry.value !== value) continue;
    if (!isReported(entry.index)) return entry.index;
    reported ??= entry.index;
  }
  return reported ?? 0;
}
