import { describe, expect, it } from 'vitest';
import { computeAddress } from 'ethers';
import { EthereumHdWallet } from './HdWalletService.js';
import { checkWalletKeys } from './walletKeys.js';
import { SidecarError } from '../errors/SidecarError.js';

// Two throwaway keys derived from the public reference mnemonic (indexes 10
// and 11) — never literal hex in the source (the pre-commit secret guard).
const MNEMONIC = `${'abandon '.repeat(11)}about`;
const wallet = new EthereumHdWallet(MNEMONIC);
const HOT_KEY = wallet.deriveSigner(10).privateKey;
const FUEL_KEY = wallet.deriveSigner(11).privateKey;
const HOT = computeAddress(HOT_KEY);
const FUEL = computeAddress(FUEL_KEY);
const COLD = wallet.derive(12).address;

function codeOf(fn: () => unknown): string | undefined {
  try {
    fn();
  } catch (err) {
    return (err as SidecarError).code;
  }
  return undefined;
}

const base = {
  hotWalletKey: null as string | null,
  fuelWalletKey: null as string | null,
  hotWalletAddress: null as string | null,
  coldWalletAddress: null as string | null,
};

describe('checkWalletKeys — the fuel wallet key is not the hot wallet key (08 §3.7.2)', () => {
  it('accepts two different keys and reports both addresses', () => {
    expect(checkWalletKeys({ ...base, hotWalletKey: HOT_KEY, fuelWalletKey: FUEL_KEY })).toEqual({
      hotWalletAddress: HOT,
      fuelWalletAddress: FUEL,
    });
  });

  it('refuses the same key twice', () => {
    expect(
      codeOf(() => checkWalletKeys({ ...base, hotWalletKey: HOT_KEY, fuelWalletKey: HOT_KEY })),
    ).toBe('WALLET_KEYS_NOT_DISTINCT');
  });

  it('refuses the same key written differently (case / 0x prefix normalised upstream)', () => {
    expect(
      codeOf(() =>
        checkWalletKeys({
          ...base,
          hotWalletKey: HOT_KEY,
          fuelWalletKey: '0x' + HOT_KEY.slice(2).toUpperCase(),
        }),
      ),
    ).toBe('WALLET_KEYS_NOT_DISTINCT');
  });

  it('refuses a fuel key that controls the configured hot or cold wallet address', () => {
    expect(
      codeOf(() =>
        checkWalletKeys({
          ...base,
          fuelWalletKey: FUEL_KEY,
          hotWalletAddress: FUEL.toLowerCase(),
        }),
      ),
    ).toBe('WALLET_KEYS_NOT_DISTINCT');
    expect(
      codeOf(() =>
        checkWalletKeys({
          ...base,
          fuelWalletKey: FUEL_KEY,
          coldWalletAddress: FUEL.toLowerCase(),
        }),
      ),
    ).toBe('WALLET_KEYS_NOT_DISTINCT');
  });

  it('accepts either key alone, or neither (T152/T153 not provisioned yet)', () => {
    expect(checkWalletKeys(base)).toEqual({ hotWalletAddress: null, fuelWalletAddress: null });
    expect(checkWalletKeys({ ...base, fuelWalletKey: FUEL_KEY }).fuelWalletAddress).toBe(FUEL);
  });
});

describe('checkWalletKeys — the hot key controls ETHEREUM_HOT_WALLET_ADDRESS', () => {
  it('accepts the matching address (canonical lowercase, as config stores it)', () => {
    expect(
      checkWalletKeys({
        ...base,
        hotWalletKey: HOT_KEY,
        hotWalletAddress: HOT.toLowerCase(),
        coldWalletAddress: COLD.toLowerCase(),
      }).hotWalletAddress,
    ).toBe(HOT);
  });

  it('refuses a hot key for another address', () => {
    expect(
      codeOf(() =>
        checkWalletKeys({
          ...base,
          hotWalletKey: HOT_KEY,
          hotWalletAddress: COLD.toLowerCase(),
        }),
      ),
    ).toBe('HOT_WALLET_ADDRESS_MISMATCH');
  });
});
