import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { config } from './config.js';
import { fakeEthereumAddress } from './ids.js';
import { spamTokenEnvelope } from './webhookEnvelopes.js';
import {
  ETHEREUM,
  TRON,
  isEvmAddress,
  profileFor,
  signingKeyFor,
  symbolForContract,
} from './networks.js';
import type { DepositPaymentAddress } from './db.js';

describe('webhook signing key follows the deposit network (T148)', () => {
  // Distinct, test-only keys: unset env leaves both empty, which would make
  // "signed with the right network's key" indistinguishable from "any key".
  const saved = {
    tron: config.blockchainWebhookSecret,
    eth: config.ethereumWebhookSecret,
  };
  beforeAll(() => {
    Object.assign(config, {
      blockchainWebhookSecret: 'unit-test-key-tron',
      ethereumWebhookSecret: 'unit-test-key-ethereum',
    });
  });
  afterAll(() => {
    Object.assign(config, {
      blockchainWebhookSecret: saved.tron,
      ethereumWebhookSecret: saved.eth,
    });
  });

  it('the two networks sign with different keys (else the tests below prove nothing)', () => {
    expect(config.ethereumWebhookSecret).not.toBe(config.blockchainWebhookSecret);
  });

  it('an Ethereum deposit is signed with the Ethereum key', () => {
    expect(signingKeyFor('ETHEREUM')).toBe(config.ethereumWebhookSecret);
  });

  it('a TRON deposit is signed with the TRON key', () => {
    expect(signingKeyFor('TRON')).toBe(config.blockchainWebhookSecret);
  });

  it('signAs overrides the deposit network (wrong-network lever)', () => {
    expect(signingKeyFor('ETHEREUM', 'TRON')).toBe(config.blockchainWebhookSecret);
    expect(signingKeyFor('TRON', 'ETHEREUM')).toBe(config.ethereumWebhookSecret);
  });
});

describe('Ethereum address check mirrors EIP-55 (sidecar-ethereum evmAddress)', () => {
  const good = fakeEthereumAddress(7);

  it('accepts all-lowercase, all-uppercase and a valid mixed-case checksum', () => {
    expect(isEvmAddress(good)).toBe(true);
    expect(isEvmAddress(good.toLowerCase())).toBe(true);
    expect(isEvmAddress('0x' + good.slice(2).toUpperCase())).toBe(true);
    expect(ETHEREUM.isAddress(good)).toBe(true);
  });

  it('refuses a mixed-case address whose checksum is wrong', () => {
    const chars = good.slice(2).split('');
    const i = chars.findIndex((c) => /[a-f]/i.test(c));
    chars[i] =
      chars[i] === chars[i].toUpperCase() ? chars[i].toLowerCase() : chars[i].toUpperCase();
    const bad = '0x' + chars.join('');
    expect(bad).not.toBe(good);
    expect(isEvmAddress(bad)).toBe(false);
    expect(ETHEREUM.isAddress(bad)).toBe(false);
  });

  it('refuses wrong length and non-hex', () => {
    expect(isEvmAddress('0x1234')).toBe(false);
    expect(isEvmAddress('0x' + 'g'.repeat(40))).toBe(false);
  });
});

describe('Ethereum contract allowlist (K2 - USDT only unless USDC is configured)', () => {
  it('accepts USDT and refuses USDC by default', () => {
    expect(config.ethereumUsdcContract).toBe('');
    expect(symbolForContract(ETHEREUM, ETHEREUM.contracts().USDT)).toBe('USDT');
    expect(symbolForContract(ETHEREUM, ETHEREUM.contracts().USDC)).toBeNull();
  });

  it('TRON keeps both tokens', () => {
    expect(symbolForContract(TRON, TRON.contracts().USDC)).toBe('USDC');
  });

  it('the wrong-token lever can still name a USDC contract', () => {
    expect(ETHEREUM.contracts().USDC).toMatch(/^0x[0-9a-f]{40}$/);
  });
});

describe('spam token contract is outside the allowlist (03 5.3a)', () => {
  const dep = {
    paymentAddressId: 'pa',
    transactionId: 'tx',
    address: 'x',
    tokenSymbol: 'USDT',
  } as unknown as DepositPaymentAddress;

  it.each([TRON, ETHEREUM])('$network: unsupportedContract is on no allowlist', (profile) => {
    expect(symbolForContract(profile, profile.unsupportedContract)).toBeNull();
    for (const c of Object.values(profile.contracts())) {
      expect(profile.sameAddress(c, profile.unsupportedContract)).toBe(false);
    }
  });

  it.each([TRON, ETHEREUM])(
    '$network: the spam envelope names it as the actual contract',
    (profile) => {
      const env = spamTokenEnvelope({
        profile: profileFor(profile.network),
        dep,
        txHash: profile.txHash('s'),
        eventIndex: 0,
        amount: '1',
      });
      expect(env.data.actualContractAddress).toBe(profile.unsupportedContract);
      expect(symbolForContract(profile, env.data.actualContractAddress)).toBeNull();
    },
  );
});
