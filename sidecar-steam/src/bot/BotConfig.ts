import { readFileSync } from 'fs';
import { logger } from '../logger.js';

/**
 * Per-bot credentials sourced from Kubernetes Secret mount (production)
 * or inline ENV JSON (development).
 *
 * Required fields cover 08 §2.5 library requirements:
 *   - accountName + password: steam-user logOn
 *   - sharedSecret: steam-totp TOTP code generation (2FA)
 *   - identitySecret: steamcommunity mobile confirmation key
 */
export interface BotCredentials {
  accountName: string;
  password: string;
  sharedSecret: string;
  identitySecret: string;
}

interface BotConfigFile {
  bots: BotCredentials[];
}

const REQUIRED_FIELDS = [
  'accountName',
  'password',
  'sharedSecret',
  'identitySecret',
] as const satisfies ReadonlyArray<keyof BotCredentials>;

function validate(record: unknown, index: number): BotCredentials {
  if (record === null || typeof record !== 'object') {
    throw new Error(`Bot config entry [${index}] is not an object`);
  }
  const obj = record as Record<string, unknown>;
  for (const field of REQUIRED_FIELDS) {
    const value = obj[field];
    if (typeof value !== 'string' || value.length === 0) {
      throw new Error(`Bot config entry [${index}] missing or empty field: ${field}`);
    }
  }
  return {
    accountName: obj.accountName as string,
    password: obj.password as string,
    sharedSecret: obj.sharedSecret as string,
    identitySecret: obj.identitySecret as string,
  };
}

function parseBotsJson(raw: string, source: string): BotCredentials[] {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch (err) {
    throw new Error(`Bot config (${source}) is not valid JSON: ${(err as Error).message}`);
  }
  if (
    parsed === null ||
    typeof parsed !== 'object' ||
    !Array.isArray((parsed as BotConfigFile).bots)
  ) {
    throw new Error(`Bot config (${source}) must contain a "bots" array`);
  }
  const list = (parsed as BotConfigFile).bots;
  return list.map((entry, idx) => validate(entry, idx));
}

/**
 * Load bot credentials from STEAM_BOTS_CONFIG_PATH (file) or STEAM_BOTS_JSON (inline).
 *
 * Resolution order:
 *   1. STEAM_BOTS_CONFIG_PATH — Kubernetes Secret mount or local file path (production)
 *   2. STEAM_BOTS_JSON — inline JSON string (development / docker-compose)
 *   3. None set → empty list (sidecar starts in skeleton mode, useful for tests / first boot)
 *
 * Returning an empty list is intentional: the sidecar still serves /health and /metrics,
 * but BotManager.selectBot() returns null and operations requiring a bot are rejected.
 */
export function loadBotCredentials(env: NodeJS.ProcessEnv = process.env): BotCredentials[] {
  const path = env.STEAM_BOTS_CONFIG_PATH?.trim();
  if (path) {
    const raw = readFileSync(path, 'utf8');
    const bots = parseBotsJson(raw, `file ${path}`);
    logger.info({ source: 'file', path, count: bots.length }, 'Bot credentials loaded');
    return bots;
  }

  const inline = env.STEAM_BOTS_JSON?.trim();
  if (inline) {
    const bots = parseBotsJson(inline, 'STEAM_BOTS_JSON');
    logger.info({ source: 'env', count: bots.length }, 'Bot credentials loaded');
    return bots;
  }

  logger.warn('No bot credentials configured (STEAM_BOTS_CONFIG_PATH / STEAM_BOTS_JSON unset)');
  return [];
}
