import { Transaction, SigningKey, type TransactionLike } from 'ethers';
import { describe, expect, it } from 'vitest';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';
import { TOKEN_ABI, encodeTransfer } from './erc20.js';
import {
  NATIVE_TRANSFER_GAS,
  assertSignableFuel,
  assertSignableTokenTransfer,
  type FuelIntent,
  type SignerPolicyConfig,
  type TokenTransferIntent,
} from './SignerPolicy.js';

/**
 * 08 §3.7.6 "İmzacı izin listesi" (T141 (d), öneriyle kaydedildi — ⚠): one
 * refusal test per rule. Each case starts from a transaction the policy
 * accepts and changes exactly one field.
 */

const USDT = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDT);
const USDC = canonicalAddress(MAINNET_TOKEN_CONTRACTS.USDC);
const RECIPIENT = '0x1111111111111111111111111111111111111111';
const DEPOSIT = '0x3333333333333333333333333333333333333333';
const HOT_WALLET = '0x4444444444444444444444444444444444444444';

const CONFIG: SignerPolicyConfig = {
  chainId: 11_155_111n,
  paymentToken: USDT,
  wrongTokenRefundToken: USDC,
  maxTokenGasLimit: 150_000n,
};

const INTENT: TokenTransferIntent = {
  contract: USDT,
  recipient: RECIPIENT,
  amountUnits: 12_345_678n,
  wrongTokenRefund: false,
  // Per transaction (yönetici kararı, T152 doğrulaması): gasLimit × maxFee ≤ cap.
  feeCapWei: 50_822n * 3_000_000_000n,
};

function token(overrides: Partial<TransactionLike> = {}): Transaction {
  return Transaction.from({
    type: 2,
    chainId: 11_155_111n,
    nonce: 0,
    gasLimit: 50_822n,
    maxFeePerGas: 2_050_000_000n,
    maxPriorityFeePerGas: 50_000_000n,
    to: USDT,
    value: 0n,
    data: encodeTransfer(RECIPIENT, 12_345_678n),
    accessList: [],
    ...overrides,
  });
}

const refusal = { code: 'SIGNER_POLICY_REJECTED' };

describe('token keys — accepted shape', () => {
  it('accepts the transfer it was asked for (type 2 and type 0)', () => {
    expect(() => assertSignableTokenTransfer(token(), INTENT, CONFIG)).not.toThrow();
    const legacy = Transaction.from({
      type: 0,
      chainId: 11_155_111n,
      nonce: 0,
      gasLimit: 50_822n,
      gasPrice: 2_000_000_000n,
      to: USDT,
      value: 0n,
      data: encodeTransfer(RECIPIENT, 12_345_678n),
    });
    expect(() => assertSignableTokenTransfer(legacy, INTENT, CONFIG)).not.toThrow();
  });
});

describe('token keys — every rule refuses', () => {
  const cases: Array<[string, () => Transaction, TokenTransferIntent?]> = [
    ['another chainId (mainnet tx on a Sepolia signer)', () => token({ chainId: 1n })],
    [
      'type 1 (EIP-2930)',
      () =>
        token({
          type: 1,
          gasPrice: 2_000_000_000n,
          maxFeePerGas: null,
          maxPriorityFeePerGas: null,
        }),
    ],
    [
      'type 4 (EIP-7702 delegation)',
      () =>
        token({
          type: 4,
          authorizationList: [
            {
              address: RECIPIENT,
              nonce: 0n,
              chainId: 11_155_111n,
              signature: new SigningKey('0x' + '22'.repeat(32)).sign('0x' + '00'.repeat(32)),
            },
          ],
        }),
    ],
    [
      'a contract that is not allowlisted',
      () => token({ to: '0x4444444444444444444444444444444444444444' }),
    ],
    ['USDC outside a wrong-token refund', () => token({ to: USDC }), { ...INTENT, contract: USDC }],
    [
      'another contract than the request names',
      () => token(),
      { ...INTENT, contract: USDC, wrongTokenRefund: true },
    ],
    ['ETH value on a token call', () => token({ value: 1n })],
    [
      'approve() instead of transfer()',
      () => token({ data: TOKEN_ABI.encodeFunctionData('isBlackListed', [RECIPIENT]) }),
    ],
    [
      'approve(spender, max)',
      () =>
        token({
          data: '0x095ea7b3' + RECIPIENT.slice(2).padStart(64, '0') + 'f'.repeat(64),
        }),
    ],
    [
      'trailing bytes after the arguments',
      () => token({ data: encodeTransfer(RECIPIENT, 12_345_678n) + '00' }),
    ],
    [
      'a dirty address word',
      () =>
        token({
          data:
            '0xa9059cbb' +
            '01' +
            RECIPIENT.slice(2).padStart(62, '0') +
            12_345_678n.toString(16).padStart(64, '0'),
        }),
    ],
    [
      'another recipient',
      () =>
        token({ data: encodeTransfer('0x2222222222222222222222222222222222222222', 12_345_678n) }),
    ],
    [
      'another amount (decimal shift)',
      () => token({ data: encodeTransfer(RECIPIENT, 12_345_678_000_000n) }),
    ],
    ['one unit more', () => token({ data: encodeTransfer(RECIPIENT, 12_345_679n) })],
    ['a gas limit above the bound', () => token({ gasLimit: 150_001n })],
    ['a fee ceiling above the cap', () => token({ maxFeePerGas: 3_000_000_001n })],
    [
      'a gas limit one above at the cap fee (worst case over the cap)',
      () => token({ gasLimit: 50_823n, maxFeePerGas: 3_000_000_000n }),
    ],
    ['an access list', () => token({ accessList: [{ address: USDT, storageKeys: [] }] })],
    ['contract creation', () => token({ to: null })],
    [
      'tip above max fee',
      () => token({ maxPriorityFeePerGas: 3_000_000_000n, maxFeePerGas: 2_000_000_000n }),
    ],
    [
      'an already signed transaction',
      () => {
        const tx = token();
        tx.signature = new SigningKey('0x' + '11'.repeat(32)).sign(tx.unsignedHash);
        return tx;
      },
    ],
  ];

  // The rule that must fire for each case — so a case is not "refused" only
  // because a later rule happened to trip (a removed type check would let a
  // type-4 transaction reach the fee rule; a lenient length check would let
  // trailing bytes reach the amount rule).
  const RULE: Record<string, string> = {
    'another chainId (mainnet tx on a Sepolia signer)': 'chainId',
    'type 1 (EIP-2930)': 'type',
    'type 4 (EIP-7702 delegation)': 'type',
    'a contract that is not allowlisted': 'contract',
    'USDC outside a wrong-token refund': 'contract',
    'another contract than the request names': 'contract',
    'ETH value on a token call': 'value',
    'approve() instead of transfer()': 'calldata',
    'approve(spender, max)': 'calldata',
    'trailing bytes after the arguments': 'calldata',
    'a dirty address word': 'calldata',
    'another recipient': 'recipient',
    'another amount (decimal shift)': 'amount',
    'one unit more': 'amount',
    'a gas limit above the bound': 'gasLimit',
    'a fee ceiling above the cap': 'feeCap',
    'a gas limit one above at the cap fee (worst case over the cap)': 'feeCap',
    'an access list': 'accessList',
    'contract creation': 'to',
    'tip above max fee': 'fees',
    'an already signed transaction': 'unsigned',
  };

  it.each(cases)('%s', (label, build, intent) => {
    expect(() => assertSignableTokenTransfer(build(), intent ?? INTENT, CONFIG)).toThrow(
      expect.objectContaining({ ...refusal, message: expect.stringContaining(`(${RULE[label]})`) }),
    );
  });

  it('the worst-case fee exactly at the cap is accepted (gasLimit × maxFee = cap)', () => {
    expect(() =>
      assertSignableTokenTransfer(token({ maxFeePerGas: 3_000_000_000n }), INTENT, CONFIG),
    ).not.toThrow();
    // A lower fee may carry a higher gas limit within the same budget.
    expect(() =>
      assertSignableTokenTransfer(
        token({ gasLimit: 101_644n, maxFeePerGas: 1_500_000_000n }),
        INTENT,
        CONFIG,
      ),
    ).not.toThrow();
  });

  it('USDC is accepted only when the request is a wrong-token refund', () => {
    const tx = token({ to: USDC });
    expect(() =>
      assertSignableTokenTransfer(
        tx,
        { ...INTENT, contract: USDC, wrongTokenRefund: true },
        CONFIG,
      ),
    ).not.toThrow();
  });

  it('nothing is signable while USDT is not configured', () => {
    expect(() =>
      assertSignableTokenTransfer(token(), INTENT, { ...CONFIG, paymentToken: '' }),
    ).toThrow(expect.objectContaining(refusal));
  });

  it('a mainnet signer refuses a Sepolia transaction', () => {
    expect(() => assertSignableTokenTransfer(token(), INTENT, { ...CONFIG, chainId: 1n })).toThrow(
      expect.objectContaining(refusal),
    );
  });
});

describe('fuel key — the one exception (T153)', () => {
  const FUEL: FuelIntent = {
    recipient: DEPOSIT,
    amountWei: 300_000_000_000_000n,
    isOwnDeposit: (a) => canonicalAddress(a) === DEPOSIT,
    maxSingleWei: 1_000_000_000_000_000n,
    maxDailyWei: 10_000_000_000_000_000n,
    spentLast24hWei: 0n,
    // T153: no more than the token send needs; its share of the fee cap.
    maxForSendWei: 300_000_000_000_000n,
    platformAddresses: [HOT_WALLET, ''],
    feeCapWei: 21_000n * 2_050_000_000n,
  };

  function fuel(overrides: Partial<TransactionLike> = {}): Transaction {
    return Transaction.from({
      type: 2,
      chainId: 11_155_111n,
      nonce: 0,
      gasLimit: NATIVE_TRANSFER_GAS,
      maxFeePerGas: 2_050_000_000n,
      maxPriorityFeePerGas: 50_000_000n,
      to: DEPOSIT,
      value: 300_000_000_000_000n,
      data: '0x',
      accessList: [],
      ...overrides,
    });
  }

  it('accepts plain ETH to an own deposit within the caps', () => {
    expect(() => assertSignableFuel(fuel(), FUEL, CONFIG)).not.toThrow();
  });

  it.each<[string, () => Transaction, Partial<FuelIntent>?]>([
    [
      'an address the service does not derive',
      () => fuel({ to: RECIPIENT }),
      { recipient: RECIPIENT },
    ],
    ['calldata on a fuel send', () => fuel({ data: encodeTransfer(DEPOSIT, 1n) })],
    ['a gas limit other than 21,000', () => fuel({ gasLimit: 21_001n })],
    ['another value than asked', () => fuel({ value: 300_000_000_000_001n })],
    [
      'above the single cap',
      () => fuel({ value: 1_000_000_000_000_001n }),
      { amountWei: 1_000_000_000_000_001n, maxForSendWei: 10n ** 18n },
    ],
    ['above the daily cap', () => fuel(), { spentLast24hWei: 9_800_000_000_000_000n }],
    ['caps not configured', () => fuel(), { maxSingleWei: null }],
    ['daily cap not configured', () => fuel(), { maxDailyWei: null }],
    ['another chain', () => fuel({ chainId: 1n })],
    ['type 4', () => fuel({ type: 4, authorizationList: [] })],
    // T153 — more than the send needs, a platform wallet, above its fee share.
    ['more than the token send needs', () => fuel(), { maxForSendWei: 300_000_000_000_000n - 1n }],
    [
      'a platform wallet the seed also derives',
      () => fuel({ to: HOT_WALLET }),
      { recipient: HOT_WALLET, isOwnDeposit: () => true },
    ],
    ['above its share of the fee cap', () => fuel(), { feeCapWei: 21_000n * 2_050_000_000n - 1n }],
  ])('%s → refused', (label, build, intent) => {
    const rule: Record<string, string> = {
      'an address the service does not derive': 'recipient',
      'calldata on a fuel send': 'calldata',
      'a gas limit other than 21,000': 'gasLimit',
      'another value than asked': 'value',
      'above the single cap': 'fuelCaps',
      'above the daily cap': 'fuelCaps',
      'caps not configured': 'fuelCaps',
      'daily cap not configured': 'fuelCaps',
      'another chain': 'chainId',
      'type 4': 'type',
      'more than the token send needs': 'amount',
      'a platform wallet the seed also derives': 'recipient',
      'above its share of the fee cap': 'feeCap',
    };
    expect(() => assertSignableFuel(build(), { ...FUEL, ...(intent ?? {}) }, CONFIG)).toThrow(
      expect.objectContaining({ ...refusal, message: expect.stringContaining(`(${rule[label]})`) }),
    );
  });
});
