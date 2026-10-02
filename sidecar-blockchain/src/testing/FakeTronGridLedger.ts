import type {
  ListTrc20Options,
  Trc20ListResponse,
  Trc20Record,
  TransactionInfo,
  TransferLogEntry,
} from '../tron/TronGridClient.js';

/**
 * Test double for the TronGrid calls the monitors make, holding a ledger of
 * TRC-20 records and answering list queries the way TronGrid does — as
 * measured on Nile (2026-10-02/03), not as assumed:
 *
 * <list type="bullet">
 *   <item>No <c>order_by</c> → newest first; <c>order: 'asc'</c> → oldest first.</item>
 *   <item><c>min_timestamp</c> is inclusive and floored to the second (a record
 *     at T is returned for T + 999, not for T + 1000).</item>
 *   <item><c>fingerprint</c> continues the same query after the last record of
 *     the previous page, in the query's order — so in the default order it
 *     leads to OLDER records. The last page carries no fingerprint, even when
 *     it is exactly <c>limit</c> long.</item>
 * </list>
 *
 * The queue-based fakes in the registry tests answer whatever was enqueued,
 * whatever the query; that is how the backward cursor went unnoticed. Tests
 * that are about which records a poll sees use this ledger instead.
 */
export interface LedgerRecord extends Trc20Record {
  /** Position of the Transfer log inside its transaction. Default 0. */
  logIndex?: number;
}

export class FakeTronGridLedger {
  private readonly records: Array<{ seq: number; record: LedgerRecord }> = [];
  private seq = 0;
  readonly listCalls: ListTrc20Options[] = [];
  readonly logLookups: string[] = [];
  /** Remaining log lookups that fail (HTTP error on the solidity node). */
  failLogLookups = 0;
  solidBlock = 0;
  readonly txInfo = new Map<string, TransactionInfo>();

  add(...records: LedgerRecord[]): void {
    for (const record of records) {
      this.records.push({ seq: this.seq++, record });
    }
  }

  /** The surface MonitorRegistry / PostCancelMonitorRegistry call. */
  readonly client = {
    // eslint-disable-next-line @typescript-eslint/require-await
    listTrc20: async (options: ListTrc20Options): Promise<Trc20ListResponse> => {
      this.listCalls.push({ ...options });
      return this.list(options);
    },
    // eslint-disable-next-line @typescript-eslint/require-await
    resolveTransferEventIndices: async (
      txHash: string,
      contractAddress: string,
      toAddress: string,
    ): Promise<TransferLogEntry[]> => {
      this.logLookups.push(txHash);
      if (this.failLogLookups > 0) {
        this.failLogLookups -= 1;
        throw new Error('TronGrid HTTP 503 Service Unavailable');
      }
      return this.records
        .map((r) => r.record)
        .filter(
          (r) =>
            r.transaction_id === txHash &&
            r.token_info.address === contractAddress &&
            r.to === toAddress,
        )
        .map((r) => ({ index: r.logIndex ?? 0, value: r.value }));
    },
    // eslint-disable-next-line @typescript-eslint/require-await
    getNowSolidBlock: async (): Promise<number> => this.solidBlock,
    // eslint-disable-next-line @typescript-eslint/require-await
    getTransactionInfoById: async (txHash: string): Promise<TransactionInfo | null> =>
      this.txInfo.get(txHash) ?? null,
  };

  private list(options: ListTrc20Options): Trc20ListResponse {
    const limit = options.limit ?? 20;
    const asc = options.order === 'asc';
    const minSecond =
      typeof options.minTimestamp === 'number'
        ? Math.floor(options.minTimestamp / 1000) * 1000
        : undefined;

    const ordered = this.records
      .filter(
        ({ record }) =>
          (record.from === options.address || record.to === options.address) &&
          (!options.contractAddress || record.token_info.address === options.contractAddress) &&
          (minSecond === undefined || record.block_timestamp >= minSecond),
      )
      .sort((a, b) => {
        const byTime = a.record.block_timestamp - b.record.block_timestamp || a.seq - b.seq;
        return asc ? byTime : -byTime;
      });

    let start = 0;
    if (options.fingerprint) {
      const afterSeq = Number(options.fingerprint.replace('after-seq-', ''));
      const at = ordered.findIndex((r) => r.seq === afterSeq);
      if (at < 0) throw new Error(`fingerprint ${options.fingerprint} not in this query`);
      start = at + 1;
    }
    const page = ordered.slice(start, start + limit);
    const more = start + limit < ordered.length;
    return {
      // The list endpoint does not carry the log position — only the log lookup does.
      records: page.map(({ record }) => {
        const listed: Trc20Record & { logIndex?: number } = { ...record };
        delete listed.logIndex;
        return listed;
      }),
      fingerprint: more ? `after-seq-${page[page.length - 1].seq}` : null,
    };
  }
}
