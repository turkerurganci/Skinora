import { describe, expect, it, vi } from 'vitest';
import type { NextFunction, Request, Response } from 'express';
import { internalKeyAuth } from './middleware.js';

/**
 * T161 — X-Internal-Key check (05 §3.4), fail closed. Before T161 an empty key
 * made this middleware call next() for every request ("dev mode").
 */
const KEY = 'k'.repeat(64);

function run(check: ReturnType<typeof internalKeyAuth>, header?: string | string[]) {
  const req = { headers: header === undefined ? {} : { 'x-internal-key': header } } as Request;
  const json = vi.fn();
  const res = { status: vi.fn(() => ({ json })) } as unknown as Response;
  const next = vi.fn() as unknown as NextFunction;
  check(req, res, next);
  return { status: (res.status as unknown as ReturnType<typeof vi.fn>).mock.calls[0]?.[0], next };
}

describe('internalKeyAuth', () => {
  it('cannot be built without a key — there is no skip-auth mode', () => {
    expect(() => internalKeyAuth('')).toThrow(/needs a key/);
  });

  it.each([
    ['no header', undefined],
    ['an empty header', ''],
    ['a wrong key of the same length', 'w'.repeat(64)],
    ['a shorter key', 'k'.repeat(63)],
    ['a longer key', 'k'.repeat(65)],
    ['a repeated header', [KEY, KEY]],
  ])('answers 401 to %s', (_label, header) => {
    const { status, next } = run(internalKeyAuth(KEY), header);
    expect(status).toBe(401);
    expect(next).not.toHaveBeenCalled();
  });

  it('passes the right key through', () => {
    const { status, next } = run(internalKeyAuth(KEY), KEY);
    expect(status).toBeUndefined();
    expect(next).toHaveBeenCalledOnce();
  });
});
