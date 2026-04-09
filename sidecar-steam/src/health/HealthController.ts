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
 * Health check endpoint.
 * In skeleton phase: always returns healthy.
 * Future tasks (T64–T69) will add real bot session and Steam API checks.
 */
export function healthCheck(_req: Request, res: Response): void {
  const checks: HealthCheck[] = [
    { name: 'steam-api', status: 'healthy', message: 'Skeleton — not yet connected' },
    { name: 'bot-session', status: 'healthy', message: 'Skeleton — no bots configured' },
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
}
