import { describe, it, expect, vi } from 'vitest';
import { TronTransferClient } from './TronTransferClient.js';
import { SidecarError } from '../errors/SidecarError.js';

function buildFakeTronWeb({
  buildOk = true,
  broadcastOk = true,
  buildMessage = 'all good',
  broadcastMessage = 'submitted',
  txid = 'fake-txid-001',
}: {
  buildOk?: boolean;
  broadcastOk?: boolean;
  buildMessage?: string;
  broadcastMessage?: string;
  txid?: string;
} = {}) {
  const triggerSmartContract = vi.fn(
    async (
      _contract: string,
      _fn: string,
      _options: { feeLimit: number; callValue: number },
      _params: Array<{ type: string; value: string }>,
      _from: string,
    ) => ({
      result: { result: buildOk, message: buildMessage },
      transaction: { raw_data: { ref: 'fake' } },
    }),
  );
  const sign = vi.fn(async (tx: unknown) => ({ ...(tx as object), signature: ['sig'] }));
  const sendRawTransaction = vi.fn(async () => ({
    result: broadcastOk,
    txid,
    message: broadcastMessage,
  }));
  const fakeTronWeb = {
    transactionBuilder: { triggerSmartContract },
    trx: { sign, sendRawTransaction },
  };
  return { fakeTronWeb, triggerSmartContract, sign, sendRawTransaction };
}

describe('TronTransferClient.sendTransfer()', () => {
  it('builds, signs and broadcasts a TRC-20 transfer and returns the txid', async () => {
    const { fakeTronWeb, triggerSmartContract, sign, sendRawTransaction } = buildFakeTronWeb();
    const client = new TronTransferClient(
      'https://nile.trongrid.io',
      'https://nile.trongrid.io',
      'apikey',
      () => fakeTronWeb,
    );

    const result = await client.sendTransfer({
      fromAddress: 'TFromAddress',
      privateKey: '01'.padStart(64, '0'),
      contractAddress: 'TContractAddress',
      toAddress: 'TToAddress',
      amountUnits: '100500000',
    });

    expect(result.txHash).toBe('fake-txid-001');
    expect(triggerSmartContract).toHaveBeenCalledOnce();
    const call = triggerSmartContract.mock.calls[0]!;
    expect(call[0]).toBe('TContractAddress');
    expect(call[1]).toBe('transfer(address,uint256)');
    expect(call[2].feeLimit).toBeGreaterThan(0);
    expect(call[3]).toEqual([
      { type: 'address', value: 'TToAddress' },
      { type: 'uint256', value: '100500000' },
    ]);
    expect(call[4]).toBe('TFromAddress');
    expect(sign).toHaveBeenCalledOnce();
    expect(sendRawTransaction).toHaveBeenCalledOnce();
  });

  it('defaults feeLimit to the configurable transferFeeLimitSun, override wins (WP10)', async () => {
    const fixtureA = buildFakeTronWeb();
    const clientA = new TronTransferClient('h', 's', '', () => fixtureA.fakeTronWeb);
    await clientA.sendTransfer({
      fromAddress: 'TFrom',
      privateKey: '01'.padStart(64, '0'),
      contractAddress: 'TC',
      toAddress: 'TTo',
      amountUnits: '1',
    });
    // Default comes from config (TRANSFER_FEE_LIMIT_SUN, 100 TRX) — no longer a
    // hardcoded magic number.
    expect(fixtureA.triggerSmartContract.mock.calls[0]![2].feeLimit).toBe(100_000_000);

    const fixtureB = buildFakeTronWeb();
    const clientB = new TronTransferClient('h', 's', '', () => fixtureB.fakeTronWeb);
    await clientB.sendTransfer({
      fromAddress: 'TFrom',
      privateKey: '01'.padStart(64, '0'),
      contractAddress: 'TC',
      toAddress: 'TTo',
      amountUnits: '1',
      options: { feeLimitSun: 7_000_000 },
    });
    expect(fixtureB.triggerSmartContract.mock.calls[0]![2].feeLimit).toBe(7_000_000);
  });

  it('throws TRANSFER_NO_PRIVATE_KEY (non-retryable) when privateKey is empty', async () => {
    const client = new TronTransferClient('h', 's', '', () => ({
      transactionBuilder: { triggerSmartContract: vi.fn() },
      trx: { sign: vi.fn(), sendRawTransaction: vi.fn() },
    }));
    await expect(
      client.sendTransfer({
        fromAddress: 'TFrom',
        privateKey: '',
        contractAddress: 'TC',
        toAddress: 'TTo',
        amountUnits: '1',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_NO_PRIVATE_KEY', retryable: false });
  });

  it('throws TRANSFER_BUILD_FAILED (retryable) when triggerSmartContract returns result=false', async () => {
    const { fakeTronWeb } = buildFakeTronWeb({
      buildOk: false,
      buildMessage: 'CONTRACT_VALIDATE_ERROR',
    });
    const client = new TronTransferClient('h', 's', '', () => fakeTronWeb);
    await expect(
      client.sendTransfer({
        fromAddress: 'TFrom',
        privateKey: '01'.padStart(64, '0'),
        contractAddress: 'TC',
        toAddress: 'TTo',
        amountUnits: '1',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_BUILD_FAILED', retryable: true });
  });

  it('throws TRANSFER_BROADCAST_REJECTED (retryable) when broadcast returns result=false', async () => {
    const { fakeTronWeb } = buildFakeTronWeb({
      broadcastOk: false,
      broadcastMessage: 'SIGERROR',
    });
    const client = new TronTransferClient('h', 's', '', () => fakeTronWeb);
    await expect(
      client.sendTransfer({
        fromAddress: 'TFrom',
        privateKey: '01'.padStart(64, '0'),
        contractAddress: 'TC',
        toAddress: 'TTo',
        amountUnits: '1',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_BROADCAST_REJECTED', retryable: true });
  });
});

describe('TronTransferClient.getTransactionStatus()', () => {
  function buildFetchMock(infoResponse: unknown, blockResponse: unknown) {
    return async (input: Parameters<typeof fetch>[0]) => {
      const url = typeof input === 'string' ? input : input.toString();
      const body = url.endsWith('gettransactioninfobyid') ? infoResponse : blockResponse;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    };
  }

  it('returns confirmation count when txBlock < currentSolidBlock', async () => {
    const fetchMock = buildFetchMock(
      { id: 'tx-1', blockNumber: 100, receipt: { result: 'SUCCESS' } },
      { block_header: { raw_data: { number: 125 } } },
    );
    const client = new TronTransferClient('h', 'https://solid', '', () => ({
      transactionBuilder: { triggerSmartContract: vi.fn() },
      trx: { sign: vi.fn(), sendRawTransaction: vi.fn() },
    }));
    const status = await client.getTransactionStatus('tx-1', fetchMock as typeof fetch);
    expect(status).toEqual({
      txHash: 'tx-1',
      blockNumber: 100,
      contractRet: 'SUCCESS',
      confirmations: 25,
    });
  });

  it('returns confirmations=-1 while the tx is not yet solidified', async () => {
    const fetchMock = buildFetchMock(
      { id: undefined },
      { block_header: { raw_data: { number: 125 } } },
    );
    const client = new TronTransferClient('h', 'https://solid', '', () => ({
      transactionBuilder: { triggerSmartContract: vi.fn() },
      trx: { sign: vi.fn(), sendRawTransaction: vi.fn() },
    }));
    const status = await client.getTransactionStatus('tx-pending', fetchMock as typeof fetch);
    expect(status.confirmations).toBe(-1);
    expect(status.blockNumber).toBeUndefined();
  });

  it('throws TRANSFER_STATUS_HTTP_ERROR on TronGrid 5xx', async () => {
    const fetchMock = async () => new Response('boom', { status: 502 });
    const client = new TronTransferClient('h', 'https://solid', '', () => ({
      transactionBuilder: { triggerSmartContract: vi.fn() },
      trx: { sign: vi.fn(), sendRawTransaction: vi.fn() },
    }));
    await expect(
      client.getTransactionStatus('tx-1', fetchMock as typeof fetch),
    ).rejects.toMatchObject({ code: 'TRANSFER_STATUS_HTTP_ERROR' } satisfies Partial<SidecarError>);
  });
});
