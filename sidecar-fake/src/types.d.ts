import 'express';

declare module 'express-serve-static-core' {
  interface Request {
    /** Set by correlationMiddleware — request-scoped correlation id. */
    correlationId?: string;
  }
}
