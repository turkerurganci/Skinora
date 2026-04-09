import { logger } from '../logger.js';

/**
 * Bot management stub — will be implemented in T64.
 * Manages multiple Steam bot sessions with capacity-based selection,
 * health checks, and failover (05 §3.2).
 */
export class BotManager {
  async initialize(): Promise<void> {
    logger.info('BotManager initialized (skeleton — no bots configured)');
  }

  async shutdown(): Promise<void> {
    logger.info('BotManager shutting down');
  }
}
