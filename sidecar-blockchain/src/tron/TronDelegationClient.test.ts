import { describe, it, expect, vi } from 'vitest';
import { TronDelegationClient, DelegationTronWebFactory } from './TronDelegationClient.js';
import { SidecarError } from '../errors/SidecarError.js';

const FULL_NODE = 'https://nile.trongrid.io';
const API_KEY = 'fake-api-key';
const OWNER_ADDR = 'TSweeperHotWallet';
const RECEIVER_ADDR = 'TDepositAddress42';
const DUMMY_OWNER_KEY = 'aa'.padStart(64, 'a');
const STAKE_ADDR = 'TStakeAccountHoldingTheFrozenTrx';

/** What a built transaction carries that the stub's signer checks: its owner. */
interface BuiltTransaction {
  txID: string;
  owner: string;
}

function buildTronWebStub(overrides: Partial<StubTronWeb> = {}): StubTronWeb {
  return {
    transactionBuilder: {
      // Each built transaction names its owner, as the real one does in
      // raw_data.contract[0].parameter.value.owner_address.
      delegateResource: vi.fn(
        async (_balance: number, _receiver: string, _resource: string, owner: string) => ({
          txID: 'delegate-tx-1',
          owner,
        }),
      ),
      undelegateResource: vi.fn(
        async (_balance: number, _receiver: string, _resource: string, owner: string) => ({
          txID: 'undelegate-tx-1',
          owner,
        }),
      ),
      sendTrx: vi.fn(async (_to: string, _amount: number, from: string) => ({
        txID: 'trx-tx-1',
        owner: from,
      })),
    },
    trx: {
      // The real TronWeb refuses here when the key does not own the
      // transaction's address — measured on Nile 2026-09-18, "Private key does
      // not match address in transaction", rejected as a bare string. The stub
      // does the same (DUMMY_OWNER_KEY owns OWNER_ADDR only), so a test that
      // wires the permission path to this signer fails the way production
      // would, instead of quietly succeeding.
      sign: vi.fn(async (transaction: BuiltTransaction) => {
        if (transaction.owner !== OWNER_ADDR) {
          return Promise.reject('Private key does not match address in transaction');
        }
        return transaction;
      }),
      multiSign: vi.fn(async (transaction: unknown) => transaction),
      sendRawTransaction: vi.fn(async () => ({ result: true, txid: 'broadcast-tx-1' })),
    },
    ...overrides,
  };
}

/** A delegation request signed on the stake account's behalf. */
function stakeRequest(permissionId: number) {
  return {
    ownerAddress: STAKE_ADDR,
    ownerPrivateKey: DUMMY_OWNER_KEY,
    ownerPermissionId: permissionId,
    receiverAddress: RECEIVER_ADDR,
    amountSun: 1,
  };
}

interface StubTronWeb {
  transactionBuilder: {
    delegateResource: ReturnType<typeof vi.fn>;
    undelegateResource: ReturnType<typeof vi.fn>;
    sendTrx: ReturnType<typeof vi.fn>;
  };
  trx: {
    sign: ReturnType<typeof vi.fn>;
    multiSign: ReturnType<typeof vi.fn>;
    sendRawTransaction: ReturnType<typeof vi.fn>;
  };
}

describe('TronDelegationClient.delegateEnergy()', () => {
  it('builds, signs and broadcasts a delegateResource call with ENERGY + lock=false', async () => {
    const tronWeb = buildTronWebStub();
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.delegateEnergy({
      ownerAddress: OWNER_ADDR,
      ownerPrivateKey: DUMMY_OWNER_KEY,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 200_000_000,
    });

    expect(result.txHash).toBe('broadcast-tx-1');
    expect(factory).toHaveBeenCalledWith(
      expect.objectContaining({
        fullHost: FULL_NODE,
        apiKey: API_KEY,
        privateKey: DUMMY_OWNER_KEY,
      }),
    );
    expect(tronWeb.transactionBuilder.delegateResource).toHaveBeenCalledWith(
      200_000_000,
      RECEIVER_ADDR,
      'ENERGY',
      OWNER_ADDR,
      false,
      undefined,
      undefined,
    );
    // No permission id: the key owns the account, so the owning signer is used
    // and the permission signer must stay untouched.
    expect(tronWeb.trx.sign).toHaveBeenCalled();
    expect(tronWeb.trx.multiSign).not.toHaveBeenCalled();
    expect(tronWeb.trx.sendRawTransaction).toHaveBeenCalled();
  });

  /**
   * The stake split (owner decision 2026-09-17): the TRX is frozen in an
   * account whose owner key is offline, and the hot wallet's key signs against
   * an active permission on it. Both halves have to reach TronWeb — the id in
   * the BUILT transaction (the chain checks the signature against that
   * permission) and the id passed to the signer.
   */
  it('signs on the stake account behalf when a permission id is configured', async () => {
    const tronWeb = buildTronWebStub();
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await client.delegateEnergy({
      ownerAddress: STAKE_ADDR,
      ownerPrivateKey: DUMMY_OWNER_KEY,
      ownerPermissionId: 2,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 200_000_000,
    });

    expect(tronWeb.transactionBuilder.delegateResource).toHaveBeenCalledWith(
      200_000_000,
      RECEIVER_ADDR,
      'ENERGY',
      STAKE_ADDR,
      false,
      undefined,
      { permissionId: 2 },
    );
    expect(tronWeb.trx.multiSign).toHaveBeenCalledWith(expect.anything(), DUMMY_OWNER_KEY, 2);
    expect(tronWeb.trx.sign).not.toHaveBeenCalled();
  });

  it('carries a non-default permission id rather than assuming 2', async () => {
    const tronWeb = buildTronWebStub();
    const client = new TronDelegationClient(FULL_NODE, API_KEY, () => tronWeb);

    await client.delegateEnergy({
      ownerAddress: STAKE_ADDR,
      ownerPrivateKey: DUMMY_OWNER_KEY,
      ownerPermissionId: 5,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 1,
    });

    expect(tronWeb.transactionBuilder.delegateResource).toHaveBeenCalledWith(
      1,
      RECEIVER_ADDR,
      'ENERGY',
      STAKE_ADDR,
      false,
      undefined,
      { permissionId: 5 },
    );
    expect(tronWeb.trx.multiSign).toHaveBeenCalledWith(expect.anything(), DUMMY_OWNER_KEY, 5);
  });

  it('rejects DELEGATE_NO_PRIVATE_KEY when key is empty', async () => {
    const tronWeb = buildTronWebStub();
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: '',
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_NO_PRIVATE_KEY', retryable: false });

    expect(tronWeb.transactionBuilder.delegateResource).not.toHaveBeenCalled();
    expect(factory).not.toHaveBeenCalled();
  });

  it('raises DELEGATE_BUILD_FAILED when transactionBuilder returns no txID', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.transactionBuilder.delegateResource.mockResolvedValueOnce(undefined);
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: DUMMY_OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_BUILD_FAILED', retryable: true });
  });

  it('raises DELEGATE_BROADCAST_REJECTED when broadcast result is false', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({
      result: false,
      message: 'BANDWIDTH_ERROR',
    });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: DUMMY_OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_BROADCAST_REJECTED', retryable: true });
  });

  it('wraps unexpected exceptions in DELEGATE_BROADCAST_FAILED (retryable)', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.transactionBuilder.delegateResource.mockRejectedValueOnce(new Error('socket reset'));
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: DUMMY_OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({
      code: 'DELEGATE_BROADCAST_FAILED',
      retryable: true,
      // Plain Error: the message passes through unwrapped.
      message: 'Energy delegation failed: socket reset',
    });
  });
});

describe('TronDelegationClient.undelegateEnergy()', () => {
  it('builds, signs and broadcasts an undelegateResource call (no lock parameter)', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValue({ result: true, txid: 'undelegate-bx-1' });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.undelegateEnergy({
      ownerAddress: OWNER_ADDR,
      ownerPrivateKey: DUMMY_OWNER_KEY,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 200_000_000,
    });

    expect(result.txHash).toBe('undelegate-bx-1');
    expect(tronWeb.transactionBuilder.undelegateResource).toHaveBeenCalledWith(
      200_000_000,
      RECEIVER_ADDR,
      'ENERGY',
      OWNER_ADDR,
      undefined,
    );
    expect(tronWeb.trx.sign).toHaveBeenCalled();
    expect(tronWeb.trx.multiSign).not.toHaveBeenCalled();
  });

  /**
   * The reclaim is signed against the same permission as the delegation. Only
   * the delegation was pinned (#325 validation): TronWeb 5.3.5 happens to write
   * a missing id into the transaction itself inside multiSign, so dropping it
   * from the builder here would be harmless today and silently wrong on the
   * next TronWeb that stops doing so.
   */
  it.each([2, 5])(
    'reclaims on the stake account behalf with permission id %i — both halves reach TronWeb',
    async (permissionId) => {
      const tronWeb = buildTronWebStub();
      const client = new TronDelegationClient(FULL_NODE, API_KEY, () => tronWeb);

      await client.undelegateEnergy(stakeRequest(permissionId));

      expect(tronWeb.transactionBuilder.undelegateResource).toHaveBeenCalledWith(
        1,
        RECEIVER_ADDR,
        'ENERGY',
        STAKE_ADDR,
        { permissionId },
      );
      expect(tronWeb.trx.multiSign).toHaveBeenCalledWith(
        expect.anything(),
        DUMMY_OWNER_KEY,
        permissionId,
      );
      expect(tronWeb.trx.sign).not.toHaveBeenCalled();
    },
  );

  it('raises UNDELEGATE_BROADCAST_REJECTED when result is false', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({
      result: false,
      code: 'CONTRACT_VALIDATE_ERROR',
    });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.undelegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: DUMMY_OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'UNDELEGATE_BROADCAST_REJECTED', retryable: true });
  });
});

describe('TronDelegationClient.sendTrx()', () => {
  it('builds, signs and broadcasts a TRX transfer used as 08 §3.3 fallback', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValue({ result: true, txid: 'trx-bx-1' });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.sendTrx({
      fromAddress: OWNER_ADDR,
      fromPrivateKey: DUMMY_OWNER_KEY,
      toAddress: RECEIVER_ADDR,
      amountSun: 15_000_000,
    });

    expect(result.txHash).toBe('trx-bx-1');
    expect(tronWeb.transactionBuilder.sendTrx).toHaveBeenCalledWith(
      RECEIVER_ADDR,
      15_000_000,
      OWNER_ADDR,
    );
  });

  it('raises FALLBACK_TRX_BROADCAST_REJECTED when broadcast rejects', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({ result: false });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.sendTrx({
        fromAddress: OWNER_ADDR,
        fromPrivateKey: DUMMY_OWNER_KEY,
        toAddress: RECEIVER_ADDR,
        amountSun: 15_000_000,
      }),
    ).rejects.toMatchObject({ code: 'FALLBACK_TRX_BROADCAST_REJECTED', retryable: true });
  });

  it('preserves SidecarError instances thrown by the builder', async () => {
    const tronWeb = buildTronWebStub();
    const upstream = new SidecarError('custom', 'CUSTOM_CODE', false);
    tronWeb.transactionBuilder.sendTrx.mockRejectedValueOnce(upstream);
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.sendTrx({
        fromAddress: OWNER_ADDR,
        fromPrivateKey: DUMMY_OWNER_KEY,
        toAddress: RECEIVER_ADDR,
        amountSun: 15_000_000,
      }),
    ).rejects.toBe(upstream);
  });
});

/**
 * TronWeb rejects a signature it will not produce with a bare string, not an
 * Error — measured on Nile 2026-09-18 by running the compiled client without a
 * permission id: the operator got "Energy delegation failed: undefined". The
 * causes that land here (wrong permission id, an account that granted none)
 * are precisely the ones whose text an operator needs. All three call sites
 * share the describer; only the delegation was pinned (#325 validation).
 */
const CALL_SITES = [
  {
    name: 'delegateEnergy',
    call: (client: TronDelegationClient) => client.delegateEnergy(stakeRequest(2)),
    signer: 'multiSign' as const,
    code: 'DELEGATE_BROADCAST_FAILED',
    prefix: 'Energy delegation failed: ',
  },
  {
    name: 'undelegateEnergy',
    call: (client: TronDelegationClient) => client.undelegateEnergy(stakeRequest(2)),
    signer: 'multiSign' as const,
    code: 'UNDELEGATE_BROADCAST_FAILED',
    prefix: 'Energy undelegation failed: ',
  },
  {
    name: 'sendTrx',
    call: (client: TronDelegationClient) =>
      client.sendTrx({
        fromAddress: OWNER_ADDR,
        fromPrivateKey: DUMMY_OWNER_KEY,
        toAddress: RECEIVER_ADDR,
        amountSun: 1,
      }),
    signer: 'sign' as const,
    code: 'FALLBACK_TRX_BROADCAST_FAILED',
    prefix: 'TRX fallback transfer failed: ',
  },
];

describe('TronDelegationClient — a rejection that is not an Error reaches the operator', () => {
  it.each(CALL_SITES)('$name: a bare string, verbatim', async ({ call, signer, code, prefix }) => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx[signer].mockRejectedValueOnce('Private key does not match address in transaction');
    const client = new TronDelegationClient(FULL_NODE, API_KEY, () => tronWeb);

    await expect(call(client)).rejects.toMatchObject({
      code,
      // Verbatim, not JSON-quoted: an operator reads this in an alert.
      message: `${prefix}Private key does not match address in transaction`,
    });
  });

  it.each(CALL_SITES)(
    '$name: an object without a message, serialised',
    async ({ call, code, prefix }) => {
      const tronWeb = buildTronWebStub();
      // TronGrid answers some rejections with a body, not a thrown Error.
      tronWeb.trx.sendRawTransaction.mockRejectedValueOnce({ code: 'SIGERROR', txid: 'abc123' });
      const client = new TronDelegationClient(FULL_NODE, API_KEY, () => tronWeb);

      await expect(call(client)).rejects.toMatchObject({
        code,
        message: `${prefix}{"code":"SIGERROR","txid":"abc123"}`,
      });
    },
  );
});
