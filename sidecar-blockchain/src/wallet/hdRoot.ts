import { HDNodeWallet, Mnemonic } from 'ethers';
import { SidecarError } from '../errors/SidecarError.js';

/**
 * BIP-32 root and child derivation shared by every blockchain service
 * (T143, T150 — 08 §3.2, §3.7.2).
 *
 * <para>
 * <b>One file, two copies, byte for byte.</b> The TRON service
 * (`sidecar-blockchain`) and the Ethereum service (`sidecar-ethereum`) run in
 * their own containers with their own seeds (11 §F8 K7), and each image is
 * built from its own directory — neither image may carry the other service's
 * code. So this module is copied rather than imported, and
 * `sidecar-ethereum/src/wallet/hdRootParity.test.ts` fails the build the
 * moment the two copies differ. Change both or neither.
 * </para>
 *
 * <para>
 * Everything a service derives — the address it reports AND the key that
 * later signs for it — goes through {@link masterNodeFromPhrase} and
 * {@link deriveChild}; there is no second root builder.
 * </para>
 */

export class HdWalletNotConfiguredError extends SidecarError {
  constructor() {
    super('Master mnemonic is not configured.', 'HD_WALLET_NOT_CONFIGURED', false);
    this.name = 'HdWalletNotConfiguredError';
  }
}

export class InvalidDerivationIndexError extends SidecarError {
  constructor(reason: string) {
    super(reason, 'INVALID_DERIVATION_INDEX', false);
    this.name = 'InvalidDerivationIndexError';
  }
}

/**
 * Largest non-hardened BIP-32 child index (2^31 - 1). Indexes at or above
 * 2^31 fall into the hardened range, which BIP-44 reserves for the purpose,
 * coin and account levels; the address level must stay below it.
 */
export const MAX_DERIVATION_INDEX = 0x7fffffff;

/** Throws {@link InvalidDerivationIndexError} unless 0 <= index <= 2^31 - 1. */
export function assertValidIndex(index: number): void {
  if (!Number.isInteger(index) || index < 0 || index > MAX_DERIVATION_INDEX) {
    throw new InvalidDerivationIndexError(
      `Index must be an integer between 0 and ${MAX_DERIVATION_INDEX} (received: ${String(index)}).`,
    );
  }
}

/**
 * Throws unless <c>node</c> is a BIP-32 master node (depth 0, path "m").
 *
 * Guards the pre-T143 defect: ethers v6 <c>HDNodeWallet.fromMnemonic(m)</c>
 * called WITHOUT a path does not return the master node — it applies its
 * default Ethereum path and returns the node at m/44'/60'/0'/0/0 (depth 5).
 * The TRON service used that node as its "root" and appended the TRON path to
 * it, so every deposit address really lived at
 * m/44'/60'/0'/0/0/44'/195'/0'/0/{i}: invisible to any standard wallet
 * restoring the same seed, while the reported path claimed m/44'/195'/0'/0/{i}.
 */
export function assertMasterNode(node: HDNodeWallet): void {
  if (node.depth !== 0 || node.path !== 'm') {
    throw new SidecarError(
      `HD root must be the BIP-32 master node (depth 0, path "m"); got depth ${node.depth}, path ${String(node.path)}.`,
      'HD_ROOT_NOT_MASTER',
      false,
    );
  }
}

/**
 * THE production root builder: the BIP-32 master node of a BIP-39 phrase
 * (empty passphrase). The explicit "m" path is load-bearing — see
 * {@link assertMasterNode}.
 *
 * A phrase that fails BIP-39 parsing throws <c>INVALID_MASTER_MNEMONIC</c>; a
 * root that is not the master node throws <c>HD_ROOT_NOT_MASTER</c>. The two
 * are kept apart on purpose: a non-master root is a code (or library) defect,
 * not a malformed phrase, and must not be reported as one. The parse error's
 * own text is not carried over — it never reaches a log line or a response,
 * so a phrase fragment it might quote cannot either.
 */
export function masterNodeFromPhrase(phrase: string): HDNodeWallet {
  let root: HDNodeWallet;
  try {
    root = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(phrase.trim()), 'm');
  } catch {
    throw new SidecarError(
      'Master mnemonic is malformed (BIP-39 word list mismatch or checksum failure).',
      'INVALID_MASTER_MNEMONIC',
      false,
    );
  }
  assertMasterNode(root);
  return root;
}

/**
 * Child of a master node at an absolute path ("m/..."). An absolute path makes
 * ethers refuse a non-master root on its own; the child's recorded path is
 * also compared with the requested one, so the path a service reports is the
 * path it actually used.
 */
export function deriveChild(root: HDNodeWallet, path: string): HDNodeWallet {
  assertMasterNode(root);
  const child = root.derivePath(path);
  if (child.path !== path) {
    throw new SidecarError(
      `Derived node path ${String(child.path)} does not match the requested path ${path}.`,
      'DERIVATION_FAILED',
      false,
    );
  }
  return child;
}
