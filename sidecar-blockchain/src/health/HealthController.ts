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
 * Future tasks (T70–T77) will add real Tron node and hot wallet checks.
 */
export function healthCheck(_req: Request, res: Response): void {
  const checks: HealthCheck[] = [
    { name: 'tron-node', status: 'healthy', message: 'Skeleton — not yet connected' },
    { name: 'hot-wallet', status: 'healthy', message: 'Skeleton — not yet configured' },
  ];

  const overallStatus = checks.every((c) => c.status === 'healthy')
    ? 'healthy'
    : checks.some((c) => c.status === 'unhealthy')
      ? 'unhealthy'
      : 'degraded';

  const response: HealthResponse = {
    status: overallStatus,
    service: 'skinora-blockchain-sidecar',
    uptime: process.uptime(),
    checks,
  };

  const statusCode = overallStatus === 'unhealthy' ? 503 : 200;
  res.status(statusCode).json(response);
}
