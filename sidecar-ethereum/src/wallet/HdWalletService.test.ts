import { afterEach, describe, expect, it, vi } from 'vitest';
import { HDNodeWallet, Mnemonic, computeAddress } from 'ethers';
import { EthereumHdWallet, derivationPath } from './HdWalletService.js';
import { InvalidDerivationIndexError, MAX_DERIVATION_INDEX } from './hdRoot.js';
import { SidecarError } from '../errors/SidecarError.js';

// BIP-39 reference mnemonic (12-word "abandon ... about", empty passphrase).
//
// Known-answer vectors from T143 (08 §3.7.2). Source: an implementation
// INDEPENDENT of this code — neither ethers nor TronWeb produced them:
// @scure/bip39 2.4.0 + @scure/bip32 2.4.0 + @noble/curves 2.4.0 +
// @noble/hashes 2.4.0 (Node 24.12.0, 2026-10-18): seed = mnemonicToSeedSync,
// master = HDKey.fromMasterSeed(seed) (depth 0), child = master.derive(path),
// address = EIP-55('0x' + keccak_256(uncompressed pubkey[1:])[-20:]).
// Same table as sidecar-blockchain/src/wallet/HdWalletService.test.ts, where
// the generator procedure is written out in full. Index 0 and 1 also match
// what public BIP-39 tools give for this mnemonic.
const TREZOR_MNEMONIC = `${'abandon '.repeat(11)}about`;

const EXPECTED: ReadonlyArray<{ readonly index: number; readonly address: string }> = [
  { index: 0, address: '0x9858EfFD232B4033E47d90003D41EC34EcaEda94' },
  { index: 1, address: '0x6Fac4D18c912343BF86fa7049364Dd4E424Ab9C0' },
  { index: 2, address: '0xb6716976A3ebe8D39aCEB04372f22Ff8e6802D7A' },
  { index: 3, address: '0xF3f50213C1d2e255e4B2bAD430F8A38EEF8D718E' },
  { index: 4, address: '0x51cA8ff9f1C0a99f88E86B8112eA3237F55374cA' },
  { index: 1_000_000, address: '0x99228504058ca9c46Cdc0cA00E7BC323454fEd2b' },
  { index: 2_147_483_647, address: '0x8848bfC75a28756B521b09afDC120BdDddC7d7c9' },
];

afterEach(() => {
  vi.restoreAllMocks();
});

describe('derivationPath()', () => {
  it("is m/44'/60'/0'/0/{index}", () => {
    expect(derivationPath(0)).toBe("m/44'/60'/0'/0/0");
    expect(derivationPath(42)).toBe("m/44'/60'/0'/0/42");
  });
});

describe('EthereumHdWallet — T143 vectors through the production path', () => {
  const wallet = new EthereumHdWallet(TREZOR_MNEMONIC);

  it.each(EXPECTED)('derives $address at index $index (EIP-55)', ({ index, address }) => {
    const result = wallet.derive(index);
    expect(result).toEqual({ address, derivationPath: `m/44'/60'/0'/0/${index}`, index });
  });

  it.each(EXPECTED)(
    'deriveSigner at index $index returns a key that controls $address',
    ({ index, address }) => {
      const signer = new EthereumHdWallet(TREZOR_MNEMONIC).deriveSigner(index);
      expect(signer.address).toBe(address);
      expect(signer.derivationPath).toBe(`m/44'/60'/0'/0/${index}`);
      expect(computeAddress(signer.privateKey)).toBe(address);
    },
  );

  it('caches the derived address per index (the key is never cached)', () => {
    const fresh = new EthereumHdWallet(TREZOR_MNEMONIC);
    expect(fresh.derive(7)).toBe(fresh.derive(7));
    expect(fresh.derive(7)).not.toHaveProperty('privateKey');
  });

  it('rejects indices outside 0..2^31-1 for derive and deriveSigner', () => {
    expect(() => wallet.derive(-1)).toThrow(InvalidDerivationIndexError);
    expect(() => wallet.derive(1.5)).toThrow(InvalidDerivationIndexError);
    expect(() => wallet.derive(MAX_DERIVATION_INDEX + 1)).toThrow(InvalidDerivationIndexError);
    expect(() => wallet.deriveSigner(MAX_DERIVATION_INDEX + 1)).toThrow(
      InvalidDerivationIndexError,
    );
  });
});

describe('EthereumHdWallet — the root is the BIP-32 master node', () => {
  function codeOf(fn: () => unknown): string | undefined {
    try {
      fn();
    } catch (err) {
      return err instanceof SidecarError ? err.code : 'NOT_A_SIDECAR_ERROR';
    }
    return undefined;
  }

  it('refuses to build with HD_ROOT_NOT_MASTER when ethers hands back a non-master node', () => {
    // The pre-T143 call shape: fromMnemonic without the explicit "m" path
    // returns the depth-5 node at m/44'/60'/0'/0/0.
    const original = HDNodeWallet.fromMnemonic.bind(HDNodeWallet);
    vi.spyOn(HDNodeWallet, 'fromMnemonic').mockImplementation((mnemonic) => original(mnemonic));

    expect(codeOf(() => new EthereumHdWallet(TREZOR_MNEMONIC))).toBe('HD_ROOT_NOT_MASTER');
  });

  it('a malformed phrase is INVALID_MASTER_MNEMONIC', () => {
    expect(codeOf(() => new EthereumHdWallet(`${'abandon '.repeat(11)}abandon`))).toBe(
      'INVALID_MASTER_MNEMONIC',
    );
  });

  it('does not reproduce the pre-T143 nested-path address for index 0', () => {
    // Nested path m/44'/60'/0'/0/0/44'/60'/0'/0/0 — what the TRON service's
    // old defect would have produced on the Ethereum path.
    const nested = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(TREZOR_MNEMONIC)).derivePath(
      "44'/60'/0'/0/0",
    );
    expect(new EthereumHdWallet(TREZOR_MNEMONIC).derive(0).address).not.toBe(nested.address);
  });
});
