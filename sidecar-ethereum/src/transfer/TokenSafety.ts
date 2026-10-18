import { SidecarError } from '../errors/SidecarError.js';
import type { EthereumRpcClient } from '../rpc/RpcClient.js';
import { TOKEN_ABI } from './erc20.js';

/**
 * Reads USDT's own switches before every send (08 §3.7.1, §3.7.6 — T142,
 * öneriyle kaydedildi — ⚠). Both checks run at the block the send is priced
 * at, against the configured USDT contract only.
 *
 * <list type="bullet">
 *   <item><b>Contract state:</b> `deprecated == false`, `paused == false`,
 *     fee switch off (`basisPointsRate == 0`, `maximumFee == 0`). Any of them
 *     otherwise → no send, `TOKEN_CONTRACT_UNSAFE` (terminal → `FAILED` +
 *     the dispatcher's admin alert). Deprecated USDT forwards calls to another
 *     contract; a fee would make the recipient receive less than sent.</item>
 *   <item><b>Recipient blacklist:</b> USDT's blacklist only checks the
 *     SENDER — a transfer TO a blacklisted address succeeds and the money is
 *     frozen there, and an `eth_call` simulation does not see it (T142
 *     measurement). So `isBlackListed(recipient)` is read; true → no send,
 *     `RECIPIENT_BLACKLISTED`.</item>
 * </list>
 */
export async function assertUsdtSendable(
  rpc: Pick<EthereumRpcClient, 'call'>,
  usdt: string,
  recipient: string,
  blockNumber: bigint,
): Promise<void> {
  const read = async <T>(fn: string, args: unknown[] = []): Promise<T> => {
    const raw = await rpc.call(
      { to: usdt, data: TOKEN_ABI.encodeFunctionData(fn, args) },
      blockNumber,
    );
    return TOKEN_ABI.decodeFunctionResult(fn, raw)[0] as T;
  };

  const [deprecated, paused, bps, maxFee] = await Promise.all([
    read<boolean>('deprecated'),
    read<boolean>('paused'),
    read<bigint>('basisPointsRate'),
    read<bigint>('maximumFee'),
  ]);
  if (deprecated || paused || bps !== 0n || maxFee !== 0n) {
    throw new SidecarError(
      `USDT contract is not safe to send through (deprecated=${deprecated}, paused=${paused}, basisPointsRate=${bps}, maximumFee=${maxFee}) — nothing sent (08 §3.7.1).`,
      'TOKEN_CONTRACT_UNSAFE',
      false,
    );
  }

  if (await read<boolean>('isBlackListed', [recipient])) {
    throw new SidecarError(
      `Recipient ${recipient} is on the USDT blacklist — a transfer would freeze the money there; nothing sent (08 §3.7.6).`,
      'RECIPIENT_BLACKLISTED',
      false,
    );
  }
}
