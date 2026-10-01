import { describe, it, expect } from 'vitest';
import {
  publishTronGridDailyRequestBudget,
  timedTronFetch,
  tronApiRequestDuration,
  tronGridDailyRequestBudget,
} from './metrics.js';
import { TronResourceClient } from './tron/TronResourceClient.js';

/**
 * T139-ActiveMonitorQuotaAlarm — the two series the tron-quota-projection
 * alert divides: the measured TronGrid request rate (the histogram's _count)
 * and the configured daily budget.
 */

async function budgetValue(): Promise<number | undefined> {
  const metric = await tronGridDailyRequestBudget.get();
  return metric.values[0]?.value;
}

async function requestCount(endpoint: string, status?: string): Promise<number> {
  const metric = await tronApiRequestDuration.get();
  return metric.values
    .filter(
      (v) =>
        v.metricName === 'skinora_blockchain_tron_api_request_duration_seconds_count' &&
        v.labels.endpoint === endpoint &&
        (status === undefined || v.labels.status === status),
    )
    .reduce((sum, v) => sum + v.value, 0);
}

function respond(ok: boolean, body: unknown = {}): typeof fetch {
  return (async () =>
    ({ ok, status: ok ? 200 : 503, json: async () => body }) as Response) as typeof fetch;
}

describe('publishTronGridDailyRequestBudget', () => {
  it('publishes the configured budget and returns it for the startup line', async () => {
    expect(publishTronGridDailyRequestBudget(250_000)).toBe(250_000);
    expect(await budgetValue()).toBe(250_000);
  });

  it.each([Number.NaN, 0, -1, 1.5])(
    'refuses %s — NaN would leave the alert in NoData, a watchman that never fires',
    (budget) => {
      expect(() => publishTronGridDailyRequestBudget(budget)).toThrow(
        /TRONGRID_DAILY_REQUEST_BUDGET/,
      );
    },
  );
});

describe('timedTronFetch', () => {
  it('counts every call under its endpoint, failures included', async () => {
    const before = await requestCount('test.endpoint');

    await timedTronFetch(respond(true), 'test.endpoint')('https://node.example/x');
    await timedTronFetch(respond(false), 'test.endpoint')('https://node.example/x');
    await expect(
      timedTronFetch(
        (async () => {
          throw new Error('socket hang up');
        }) as typeof fetch,
        'test.endpoint',
      )('https://node.example/x'),
    ).rejects.toThrow('socket hang up');

    expect((await requestCount('test.endpoint')) - before).toBe(3);
  });
});

describe('TronResourceClient — its probes spend the same quota and are counted', () => {
  it('counts a getaccountresource call under wallet.getaccountresource', async () => {
    const before = await requestCount('wallet.getaccountresource', 'ok');

    await new TronResourceClient('https://nile.example', 'key').getAccountResources(
      'TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD',
      respond(true),
    );

    expect((await requestCount('wallet.getaccountresource', 'ok')) - before).toBe(1);
  });

  it('counts getchainparameters too', async () => {
    const before = await requestCount('wallet.getchainparameters');

    await new TronResourceClient('https://nile.example', 'key').getChainFeeParameters(
      respond(true, {
        chainParameter: [
          { key: 'getEnergyFee', value: 100 },
          { key: 'getTransactionFee', value: 1_000 },
        ],
      }),
    );

    expect((await requestCount('wallet.getchainparameters')) - before).toBe(1);
  });
});
