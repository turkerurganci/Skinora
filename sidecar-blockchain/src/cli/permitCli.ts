import type { DepositRefundPermitStore } from '../transfer/DepositRefundPermits.js';

/**
 * T172b — argument handling of the deposit refund permit command, shared by
 * the TRON and Ethereum entry points (byte-identical in both services).
 * Prints one JSON document to stdout; returns the exit code.
 */
export function runPermitCli(
  argv: readonly string[],
  open: () => DepositRefundPermitStore,
  maxValidityMs: number,
  out: (line: string) => void = (line) => process.stdout.write(line + '\n'),
): number {
  const [command, ...rest] = argv;
  const args = new Map<string, string>();
  for (let i = 0; i < rest.length; i += 2) {
    const key = rest[i];
    const value = rest[i + 1];
    if (!key?.startsWith('--') || value === undefined) {
      out(JSON.stringify({ error: `Expected --name value pairs, got "${key ?? ''}".` }));
      return 2;
    }
    args.set(key.slice(2), value);
  }
  try {
    const store = open();
    switch (command) {
      case 'create': {
        const hours = args.has('hours') ? Number(args.get('hours')) : maxValidityMs / 3_600_000;
        if (!Number.isFinite(hours)) throw new Error('--hours must be a number.');
        const permit = store.create({
          blockchainTransactionId: args.get('tx') ?? '',
          recipient: args.get('to') ?? '',
          amount: args.get('amount') ?? '',
          validForMs: Math.round(hours * 3_600_000),
          createdBy: args.get('by') ?? '',
          reason: args.get('reason') ?? '',
        });
        out(JSON.stringify({ created: permit }, null, 2));
        return 0;
      }
      case 'list':
        out(JSON.stringify({ permits: store.list() }, null, 2));
        return 0;
      case 'revoke':
        out(JSON.stringify({ revoked: store.revoke(args.get('id') ?? '') }, null, 2));
        return 0;
      default:
        out(
          JSON.stringify({
            error:
              'Usage: create --tx <id> --to <address> --amount <decimal> --by <operator> --reason <text> [--hours 1-24] | list | revoke --id <permit id>',
          }),
        );
        return 2;
    }
  } catch (err) {
    out(JSON.stringify({ error: (err as Error).message }));
    return 1;
  }
}
