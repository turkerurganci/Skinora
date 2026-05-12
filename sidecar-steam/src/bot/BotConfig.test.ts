import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { mkdtempSync, rmSync, writeFileSync } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';

vi.mock('../logger.js', () => ({
  logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn(), debug: vi.fn() },
}));

import { loadBotCredentials } from './BotConfig.js';

describe('loadBotCredentials', () => {
  let workDir: string;

  beforeEach(() => {
    workDir = mkdtempSync(join(tmpdir(), 'skinora-bot-config-'));
  });

  afterEach(() => {
    rmSync(workDir, { recursive: true, force: true });
  });

  it('returns empty list when neither STEAM_BOTS_CONFIG_PATH nor STEAM_BOTS_JSON is set', () => {
    const bots = loadBotCredentials({});
    expect(bots).toEqual([]);
  });

  it('parses inline JSON from STEAM_BOTS_JSON', () => {
    const bots = loadBotCredentials({
      STEAM_BOTS_JSON: JSON.stringify({
        bots: [
          {
            accountName: 'bot1',
            password: 'pw1',
            sharedSecret: 'ss1',
            identitySecret: 'is1',
          },
        ],
      }),
    });
    expect(bots).toEqual([
      { accountName: 'bot1', password: 'pw1', sharedSecret: 'ss1', identitySecret: 'is1' },
    ]);
  });

  it('reads from STEAM_BOTS_CONFIG_PATH file', () => {
    const path = join(workDir, 'bots.json');
    writeFileSync(
      path,
      JSON.stringify({
        bots: [
          {
            accountName: 'fileBot',
            password: 'pw',
            sharedSecret: 'ss',
            identitySecret: 'is',
          },
        ],
      }),
    );
    const bots = loadBotCredentials({ STEAM_BOTS_CONFIG_PATH: path });
    expect(bots).toHaveLength(1);
    expect(bots[0].accountName).toBe('fileBot');
  });

  it('prefers STEAM_BOTS_CONFIG_PATH over STEAM_BOTS_JSON when both are set', () => {
    const path = join(workDir, 'bots.json');
    writeFileSync(
      path,
      JSON.stringify({
        bots: [{ accountName: 'fileBot', password: 'p', sharedSecret: 's', identitySecret: 'i' }],
      }),
    );
    const bots = loadBotCredentials({
      STEAM_BOTS_CONFIG_PATH: path,
      STEAM_BOTS_JSON: JSON.stringify({
        bots: [{ accountName: 'envBot', password: 'p', sharedSecret: 's', identitySecret: 'i' }],
      }),
    });
    expect(bots).toHaveLength(1);
    expect(bots[0].accountName).toBe('fileBot');
  });

  it('throws when JSON is malformed', () => {
    expect(() => loadBotCredentials({ STEAM_BOTS_JSON: '{not valid' })).toThrow(/not valid JSON/i);
  });

  it('throws when "bots" array is missing', () => {
    expect(() => loadBotCredentials({ STEAM_BOTS_JSON: '{}' })).toThrow(/"bots" array/);
  });

  it('throws when a required field is missing', () => {
    expect(() =>
      loadBotCredentials({
        STEAM_BOTS_JSON: JSON.stringify({
          bots: [{ accountName: 'bot1', password: 'pw', sharedSecret: 'ss' }],
        }),
      }),
    ).toThrow(/identitySecret/);
  });

  it('throws when a required field is empty string', () => {
    expect(() =>
      loadBotCredentials({
        STEAM_BOTS_JSON: JSON.stringify({
          bots: [
            {
              accountName: 'bot1',
              password: 'pw',
              sharedSecret: '',
              identitySecret: 'is',
            },
          ],
        }),
      }),
    ).toThrow(/sharedSecret/);
  });

  it('parses multiple bots preserving order', () => {
    const bots = loadBotCredentials({
      STEAM_BOTS_JSON: JSON.stringify({
        bots: [
          { accountName: 'a', password: 'p', sharedSecret: 's', identitySecret: 'i' },
          { accountName: 'b', password: 'p', sharedSecret: 's', identitySecret: 'i' },
          { accountName: 'c', password: 'p', sharedSecret: 's', identitySecret: 'i' },
        ],
      }),
    });
    expect(bots.map((b) => b.accountName)).toEqual(['a', 'b', 'c']);
  });
});
