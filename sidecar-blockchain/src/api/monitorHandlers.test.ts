import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect, vi } from 'vitest';
import type { Request, Response } from 'express';
import { startMonitorHandler } from './monitorHandlers.js';
import type { MonitorRegistry, MonitorStartOptions } from '../monitor/MonitorRegistry.js';

/**
 * T139-ActiveMonitorQuotaAlarm — the start request carries the cadence the
 * address is polled at (owner decision 2026-10-02, 08 §3.4).
 *
 * The bodies come from `contracts/monitor-start/`, the same files the
 * backend's contract test produces byte for byte from its real client
 * (BlockchainSidecarMonitorStartContractTests), so neither side can drift on
 * the field name or its two spellings without the other's test failing.
 */
const CONTRACT_DIR = resolve(__dirname, '../../contracts/monitor-start');

function loadExample(name: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(CONTRACT_DIR, name), 'utf8')) as Record<string, unknown>;
}

interface Captured {
  statusCode: number;
  body: unknown;
}

function call(body: unknown): { captured: Captured; started: MonitorStartOptions[] } {
  const started: MonitorStartOptions[] = [];
  const registry = {
    start: vi.fn((options: MonitorStartOptions) => {
      started.push(options);
      return { started: true, cadence: options.cadence ?? 'PAYMENT' };
    }),
  } as unknown as MonitorRegistry;
  const captured: Captured = { statusCode: 0, body: undefined };
  const res = {
    status(code: number) {
      captured.statusCode = code;
      return this;
    },
    json(payload: unknown) {
      captured.body = payload;
      return this;
    },
  } as unknown as Response;

  startMonitorHandler(registry)({ body } as Request, res);
  return { captured, started };
}

describe('startMonitorHandler — cadence', () => {
  it.each([
    ['payment.request.json', 'PAYMENT'],
    ['holding.request.json', 'HOLDING'],
  ])('accepts the backend body %s and arms the address at %s', (file, cadence) => {
    const { captured, started } = call(loadExample(file));

    expect(captured.statusCode).toBe(200);
    expect(started).toHaveLength(1);
    expect(started[0].cadence).toBe(cadence);
    expect(captured.body).toMatchObject({ acknowledged: true, cadence });
  });

  it('arms at PAYMENT when the body names no cadence — a backend predating cadences', () => {
    const body = loadExample('payment.request.json');
    delete body.cadence;

    const { captured, started } = call(body);

    expect(captured.statusCode).toBe(200);
    expect(started[0].cadence).toBe('PAYMENT');
  });

  it.each([['holding'], ['SLOW'], [''], [15], [null]])(
    'rejects cadence %j instead of guessing one of the two costs',
    (cadence) => {
      const body = { ...loadExample('holding.request.json'), cadence };

      const { captured, started } = call(body);

      expect(captured.statusCode).toBe(400);
      expect(captured.body).toMatchObject({ error: 'UNSUPPORTED_CADENCE' });
      expect(started).toHaveLength(0);
    },
  );
});
