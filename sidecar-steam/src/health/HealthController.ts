import type { Request, Response } from 'express';

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
 *
 * T133 — the `bot-session` check went with the bot pool. It reported
 * `degraded` whenever no credentials were configured, which is now the ONLY
 * possible state: the sidecar holds no Steam account (05 §3.2) and a
 * credential-less boot is the supported configuration, not a deficiency. A
 * check that is permanently degraded is not a signal, and it would keep the
 * container's compose healthcheck reading `degraded` forever.
 */
export function healthCheckFactory() {
  return function healthCheck(_req: Request, res: Response): void {
    const checks: HealthCheck[] = [
      { name: 'steam-api', status: 'healthy', message: 'Connectivity probe deferred to T67' },
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
