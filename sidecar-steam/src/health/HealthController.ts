import type { Request, Response } from 'express';
import type { BotManager, BotPoolSnapshot } from '../bot/BotManager.js';

interface HealthCheck {
  name: string;
  status: 'healthy' | 'degraded' | 'unhealthy';
  message?: string;
}

interface HealthResponse {
  status: 'healthy' | 'degraded' | 'unhealthy';
  service: string;
  uptime: number;
  checks: HealthCheck[];
}

/**
 * Factory for the `/health` handler.
 * `botManager` is optional so tests can exercise the no-bots path easily.
 */
export function healthCheckFactory(botManager?: BotManager) {
  return function healthCheck(_req: Request, res: Response): void {
    const snapshot = botManager?.snapshot();
    const checks: HealthCheck[] = [
      { name: 'steam-api', status: 'healthy', message: 'Connectivity probe deferred to T67' },
      buildBotSessionCheck(snapshot),
    ];

    const overallStatus = checks.every((c) => c.status === 'healthy')
      ? 'healthy'
      : checks.some((c) => c.status === 'unhealthy')
        ? 'unhealthy'
        : 'degraded';

    const response: HealthResponse = {
      status: overallStatus,
      service: 'skinora-steam-sidecar',
      uptime: process.uptime(),
      checks,
    };

    const statusCode = overallStatus === 'unhealthy' ? 503 : 200;
    res.status(statusCode).json(response);
  };
}

/**
 * Factory for `/api/bots/status` — detailed pool state.
 * Mounted behind internalKeyAuth in routes.ts.
 */
export function botStatusFactory(botManager?: BotManager) {
  return function botStatus(_req: Request, res: Response): void {
    if (!botManager) {
      res.json({ healthy: 0, total: 0, removed: 0, bots: [] });
      return;
    }
    res.json(botManager.snapshot());
  };
}

function buildBotSessionCheck(snapshot?: BotPoolSnapshot): HealthCheck {
  if (!snapshot || snapshot.total === 0) {
    return {
      name: 'bot-session',
      status: 'degraded',
      message: 'No bots configured (sidecar idle)',
    };
  }
  if (snapshot.healthy === 0) {
    return {
      name: 'bot-session',
      status: 'unhealthy',
      message: `0/${snapshot.total} bots ready`,
    };
  }
  if (snapshot.healthy < snapshot.total) {
    return {
      name: 'bot-session',
      status: 'degraded',
      message: `${snapshot.healthy}/${snapshot.total} bots ready`,
    };
  }
  return {
    name: 'bot-session',
    status: 'healthy',
    message: `${snapshot.healthy}/${snapshot.total} bots ready`,
  };
}
