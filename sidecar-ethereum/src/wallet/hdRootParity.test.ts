import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * T150 — "Kök düğüm TRON ve Ethereum için tek bir üretim fonksiyonundan
 * kuruluyor". The two services live in separate containers built from
 * separate directories (11 §F8 K7), so the root builder cannot be imported
 * across them; it is copied. This test is what makes the copy one function:
 * any difference between the two files fails the build. Line endings are
 * normalised (a Windows checkout may hold CRLF; git stores LF).
 */
const read = (path: string) => readFileSync(path, 'utf8').replace(/\r\n/g, '\n');

describe('hdRoot.ts — one root builder for both networks', () => {
  it('is byte-identical in the TRON and the Ethereum service', () => {
    const ethereum = read(resolve(__dirname, 'hdRoot.ts'));
    const tron = read(resolve(__dirname, '../../../sidecar-blockchain/src/wallet/hdRoot.ts'));
    expect(ethereum).toBe(tron);
  });

  it('imports only what both services provide at the same path', () => {
    const source = read(resolve(__dirname, 'hdRoot.ts'));
    const imports = [...source.matchAll(/^import .* from '([^']+)';$/gm)].map((m) => m[1]);
    expect(imports.sort()).toEqual(['../errors/SidecarError.js', 'ethers']);
  });
});
