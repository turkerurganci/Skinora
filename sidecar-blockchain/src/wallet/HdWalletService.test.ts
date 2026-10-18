import { afterEach, describe, it, expect, vi } from 'vitest';
import { HDNodeWallet, Mnemonic } from 'ethers';
import TronWeb from 'tronweb';
import {
  HdWalletNotConfiguredError,
  HdWalletService,
  InvalidDerivationIndexError,
  MAX_DERIVATION_INDEX,
  assertMasterNode,
  derivationPath,
  masterNodeFromPhrase,
} from './HdWalletService.js';
import { deriveChild } from './hdRoot.js';
import { SidecarError } from '../errors/SidecarError.js';

// BIP-39 reference mnemonic (12-word "abandon ... about", empty passphrase).
//
// Known-answer vectors (T143). Source: an implementation INDEPENDENT of this
// code — neither ethers nor TronWeb was used to produce them.
//
// Generator: @scure/bip39 2.4.0 + @scure/bip32 2.4.0 + @noble/curves 2.4.0 +
// @noble/hashes 2.4.0 + @scure/base 2.4.0 (npm, Node 24.12.0), run in a
// scratch folder outside the repo on 2026-10-18. Procedure:
//   1. seed = bip39.mnemonicToSeedSync(TREZOR_MNEMONIC, '')        (BIP-39)
//      -> 5eb00bbddcf06908...ce9e38e4 (64 bytes)
//   2. master = HDKey.fromMasterSeed(seed); master.depth === 0      (BIP-32)
//   3. child = master.derive(path)                                  (BIP-44)
//   4. pub = secp256k1.getPublicKey(child.privateKey, false)        (65 bytes)
//   5. a20 = keccak_256(pub.slice(1)).slice(-20)
//   6. TRON: createBase58check(sha256).encode(0x41 || a20)
//      Ethereum: '0x' + hex(a20) with EIP-55 checksum casing
// The TRON index-0 address and the Ethereum index-0/1 addresses also match
// the values public BIP-39 tools give for this mnemonic.
//
// The same generator, run on the pre-T143 path m/44'/60'/0'/0/0/44'/195'/0'/0/{i},
// reproduces exactly the five addresses the old version of this file asserted
// (and wrongly attributed to the standard path); index 0 of that path is
// PRE_T143_INDEX0_ADDRESS below.
const TREZOR_MNEMONIC = `${'abandon '.repeat(11)}about`;

// TRON, m/44'/195'/0'/0/{index}.
const EXPECTED_ADDRESSES: ReadonlyArray<{ readonly index: number; readonly address: string }> = [
  { index: 0, address: 'TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH' },
  { index: 1, address: 'TSeJkUh4Qv67VNFwY8LaAxERygNdy6NQZK' },
  { index: 2, address: 'TYJPRrdB5APNeRs4R7fYZSwW3TcrTKw2gx' },
  { index: 3, address: 'TRhVWK5XEDkQBDevcdCWW7RW51aRncty4W' },
  { index: 4, address: 'TT2X2yyubp7qpAWYYNE5JQWBtoZ7ikQFsY' },
  { index: 1_000_000, address: 'TLyD19fUG6pKu6yRQNgH3TKnnoBJb1xVH4' },
  { index: 2_147_483_647, address: 'TVapZk5c7S7ARtSBi9jD13rhkbZke4F4sa' },
];

// Ethereum, m/44'/60'/0'/0/{index}. K7: the Ethereum service derives these
// from its OWN seed (T150); here the shared master-node primitive is pinned
// on the Ethereum path with the reference mnemonic.
const EXPECTED_ETHEREUM_ADDRESSES: ReadonlyArray<{
  readonly index: number;
  readonly address: string;
}> = [
  { index: 0, address: '0x9858EfFD232B4033E47d90003D41EC34EcaEda94' },
  { index: 1, address: '0x6Fac4D18c912343BF86fa7049364Dd4E424Ab9C0' },
  { index: 2, address: '0xb6716976A3ebe8D39aCEB04372f22Ff8e6802D7A' },
  { index: 3, address: '0xF3f50213C1d2e255e4B2bAD430F8A38EEF8D718E' },
  { index: 4, address: '0x51cA8ff9f1C0a99f88E86B8112eA3237F55374cA' },
  { index: 1_000_000, address: '0x99228504058ca9c46Cdc0cA00E7BC323454fEd2b' },
  { index: 2_147_483_647, address: '0x8848bfC75a28756B521b09afDC120BdDddC7d7c9' },
];

// Address the pre-T143 code produced for index 0 (nested path above).
const PRE_T143_INDEX0_ADDRESS = 'TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu';

describe('derivationPath()', () => {
  it("uses Tron BIP-44 path m/44'/195'/0'/0/{index}", () => {
    expect(derivationPath(0)).toBe("m/44'/195'/0'/0/0");
    expect(derivationPath(42)).toBe("m/44'/195'/0'/0/42");
    expect(derivationPath(999_999)).toBe("m/44'/195'/0'/0/999999");
  });
});

describe('master node (T143 regression guard)', () => {
  it('ethers fromMnemonic WITHOUT a path returns a depth-5 node, not the master', () => {
    // Pins the library behaviour the pre-T143 code fell into: if a future
    // ethers release changes it, this test says so instead of the paths
    // shifting silently.
    const node = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(TREZOR_MNEMONIC));
    expect(node.depth).toBe(5);
    expect(node.path).toBe("m/44'/60'/0'/0/0");
    expect(() => assertMasterNode(node)).toThrow(/HD root must be the BIP-32 master node/);
  });

  it('masterNodeFromPhrase returns the BIP-32 master node (depth 0, path "m")', () => {
    const root = masterNodeFromPhrase(TREZOR_MNEMONIC);
    expect(root.depth).toBe(0);
    expect(root.path).toBe('m');
    expect(() => assertMasterNode(root)).not.toThrow();
  });

  it('the pre-T143 index-0 address is reachable only through the nested path', () => {
    const buggyRoot = HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(TREZOR_MNEMONIC));
    const nested = buggyRoot.derivePath("44'/195'/0'/0/0");
    expect(nested.path).toBe("m/44'/60'/0'/0/0/44'/195'/0'/0/0");
    expect(TronWeb.address.fromPrivateKey(nested.privateKey.slice(2))).toBe(
      PRE_T143_INDEX0_ADDRESS,
    );
    expect(new HdWalletService(TREZOR_MNEMONIC).derive(0).address).not.toBe(
      PRE_T143_INDEX0_ADDRESS,
    );
  });

  it.each(EXPECTED_ETHEREUM_ADDRESSES)(
    "derives the known Ethereum address at m/44'/60'/0'/0/$index through the production root and child builders",
    ({ index, address }) => {
      // T150: masterNodeFromPhrase IS the root builder HdWalletService uses
      // (getRoot delegates to it) and deriveChild is its child step, so these
      // vectors run through the production path. The Ethereum service runs
      // the same vectors through its own copy of hdRoot.ts.
      const path = `m/44'/60'/0'/0/${index}`;
      const child = deriveChild(masterNodeFromPhrase(TREZOR_MNEMONIC), path);
      expect(child.path).toBe(path);
      expect(child.address).toBe(address);
    },
  );
});

/**
 * T150 (T143 validation, non-blocking finding 2): the master-node guard must
 * be exercised through the SERVICE, not only through the helper. ethers is
 * made to ignore the explicit "m" path — the pre-T143 call shape — so the
 * service is handed the depth-5 node at m/44'/60'/0'/0/0 as its "root".
 */
describe('HdWalletService — master-node guard through the service', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  function returnDefaultPathNodeInsteadOfMaster(): void {
    const original = HDNodeWallet.fromMnemonic.bind(HDNodeWallet);
    vi.spyOn(HDNodeWallet, 'fromMnemonic').mockImplementation((mnemonic) => original(mnemonic));
  }

  it.each(['derive', 'deriveSigner'] as const)(
    '%s refuses with HD_ROOT_NOT_MASTER when the root is not the master node',
    (method) => {
      returnDefaultPathNodeInsteadOfMaster();
      const service = new HdWalletService(TREZOR_MNEMONIC);

      let caught: unknown;
      try {
        service[method](0);
      } catch (err) {
        caught = err;
      }
      expect(caught).toBeInstanceOf(SidecarError);
      expect((caught as SidecarError).code).toBe('HD_ROOT_NOT_MASTER');
      expect(HDNodeWallet.fromMnemonic).toHaveBeenCalled();
    },
  );

  it('the same service derives the standard address once the root is the master node', () => {
    // Control: without the mock the identical call succeeds, so the refusal
    // above comes from the root check, not from the phrase or the index.
    expect(new HdWalletService(TREZOR_MNEMONIC).derive(0).address).toBe(
      EXPECTED_ADDRESSES[0].address,
    );
  });

  it('a malformed phrase is INVALID_MASTER_MNEMONIC, never HD_ROOT_NOT_MASTER', () => {
    const service = new HdWalletService(`${'abandon '.repeat(11)}abandon`);
    let caught: unknown;
    try {
      service.derive(0);
    } catch (err) {
      caught = err;
    }
    expect((caught as SidecarError).code).toBe('INVALID_MASTER_MNEMONIC');
  });
});

describe('HdWalletService', () => {
  describe('configured', () => {
    const service = new HdWalletService(TREZOR_MNEMONIC);

    it('reports configured when a mnemonic is present', () => {
      expect(service.isConfigured()).toBe(true);
    });

    it.each(EXPECTED_ADDRESSES)(
      'derives the known Tron address for the Trezor reference mnemonic at index $index',
      ({ index, address }) => {
        const result = service.derive(index);
        expect(result.address).toBe(address);
        expect(result.derivationPath).toBe(`m/44'/195'/0'/0/${index}`);
        expect(result.index).toBe(index);
      },
    );

    it.each(EXPECTED_ADDRESSES)(
      'deriveSigner returns the known address and a key that controls it at index $index',
      ({ index, address }) => {
        const signer = new HdWalletService(TREZOR_MNEMONIC).deriveSigner(index);
        expect(signer.address).toBe(address);
        expect(signer.derivationPath).toBe(`m/44'/195'/0'/0/${index}`);
        expect(TronWeb.address.fromPrivateKey(signer.privateKey)).toBe(address);
      },
    );

    it('deriveSigner index-0 key matches the independent generator (prefix)', () => {
      // A full 64-hex key literal is blocked by the pre-commit secret guard
      // even for the public reference mnemonic; the 16-hex prefix plus the
      // address checks above pin the key.
      expect(service.deriveSigner(0).privateKey.startsWith('b5a4cea271ff424d')).toBe(true);
    });

    it('returns a Tron base58 address (T-prefix, 34 chars)', () => {
      const result = service.derive(0);
      expect(result.address).toMatch(/^T[1-9A-HJ-NP-Za-km-z]{33}$/);
    });

    it('produces deterministic output for the same index across calls', () => {
      const first = service.derive(7);
      const second = service.derive(7);
      expect(first.address).toBe(second.address);
    });

    it('caches the derived address per index (WP10 — returns the same instance)', () => {
      const fresh = new HdWalletService(TREZOR_MNEMONIC);
      const first = fresh.derive(42);
      const second = fresh.derive(42);
      // Same object reference proves the cache hit (no re-derivation).
      expect(second).toBe(first);
      // A different index is still derived independently.
      expect(fresh.derive(43)).not.toBe(first);
    });

    it('produces distinct addresses for adjacent indices', () => {
      const a = service.derive(100);
      const b = service.derive(101);
      expect(a.address).not.toBe(b.address);
    });

    it('rejects negative indices', () => {
      expect(() => service.derive(-1)).toThrow(InvalidDerivationIndexError);
    });

    it('rejects non-integer indices', () => {
      expect(() => service.derive(1.5)).toThrow(InvalidDerivationIndexError);
      expect(() => service.derive(Number.NaN)).toThrow(InvalidDerivationIndexError);
    });

    it('rejects indices in the hardened range (>= 2^31) for derive and deriveSigner', () => {
      expect(MAX_DERIVATION_INDEX).toBe(2_147_483_647);
      expect(() => service.derive(MAX_DERIVATION_INDEX + 1)).toThrow(InvalidDerivationIndexError);
      expect(() => service.deriveSigner(MAX_DERIVATION_INDEX + 1)).toThrow(
        InvalidDerivationIndexError,
      );
      expect(() => service.deriveSigner(-1)).toThrow(InvalidDerivationIndexError);
    });
  });

  describe('not configured', () => {
    it('throws HdWalletNotConfiguredError when the mnemonic is empty', () => {
      const service = new HdWalletService('');
      expect(service.isConfigured()).toBe(false);
      expect(() => service.derive(0)).toThrow(HdWalletNotConfiguredError);
    });

    it('throws HdWalletNotConfiguredError when the mnemonic is whitespace only', () => {
      const service = new HdWalletService('   \n  ');
      expect(service.isConfigured()).toBe(false);
      expect(() => service.derive(0)).toThrow(HdWalletNotConfiguredError);
    });
  });

  describe('invalid mnemonic', () => {
    it('rejects a phrase that fails BIP-39 checksum on first derive', () => {
      // 12 valid wordlist words but invalid checksum (changed last word).
      const service = new HdWalletService(`${'abandon '.repeat(11)}abandon`);
      expect(() => service.derive(0)).toThrow(/INVALID_MASTER_MNEMONIC|invalid|checksum/i);
    });
  });
});
