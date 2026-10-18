import type pino from 'pino';

declare global {
  namespace Express {
    interface Request {
      log: pino.Logger;
      correlationId: string;
    }
  }
}
