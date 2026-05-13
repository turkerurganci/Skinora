import { describe, it, expect } from 'vitest';
import type {
  WebhookPayload,
  BotEventName,
  BotEventPayload,
  BotSessionFailedData,
  BotRemovedFromPoolData,
} from './WebhookPayloads.js';
import type { BotSessionStatus } from '../bot/BotSession.js';

/**
 * Contract test: pin the exact event names and payload shape that the
 * .NET backend will deserialize once T68 lands. Renaming an event here
 * without updating the backend would break the cross-service contract,
 * so this test acts as a tripwire.
 */
describe('Webhook payload contract', () => {
  const status: BotSessionStatus = {
    accountName: 'bot1',
    state: 'FAILED',
    lastTransitionAt: '2026-05-12T00:00:00Z',
    retryCount: 3,
    lastError: 'eresult=5',
  };

  it('pins the bot event name set', () => {
    const names: BotEventName[] = ['bot.session_failed', 'bot.removed_from_pool'];
    expect(names).toEqual(['bot.session_failed', 'bot.removed_from_pool']);
  });

  it('bot.session_failed payload has reason + status fields', () => {
    const data: BotSessionFailedData = { reason: 'login_failed', status };
    const payload: BotEventPayload<BotSessionFailedData> = {
      event: 'bot.session_failed',
      timestamp: '2026-05-12T00:00:00Z',
      data: { ...data },
    };
    expect(payload.event).toBe('bot.session_failed');
    expect(payload.data.reason).toBe('login_failed');
    expect((payload.data as unknown as BotSessionFailedData).status.accountName).toBe('bot1');
  });

  it('bot.removed_from_pool payload has accountName + reason + status fields', () => {
    const data: BotRemovedFromPoolData = {
      accountName: 'bot1',
      reason: 'session_recovery_failed',
      status,
    };
    const payload: BotEventPayload<BotRemovedFromPoolData> = {
      event: 'bot.removed_from_pool',
      timestamp: '2026-05-12T00:00:00Z',
      data: { ...data },
    };
    expect(payload.event).toBe('bot.removed_from_pool');
    expect(payload.data.accountName).toBe('bot1');
    expect(payload.data.reason).toBe('session_recovery_failed');
  });

  it('WebhookPayload envelope shape is preserved', () => {
    const generic: WebhookPayload = {
      event: 'bot.session_failed',
      timestamp: '2026-05-12T00:00:00Z',
      data: { foo: 'bar' },
    };
    expect(Object.keys(generic).sort()).toEqual(['data', 'event', 'timestamp']);
  });
});
