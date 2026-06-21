import jwt from 'jsonwebtoken';
import { e2eConfig } from './config';

/**
 * Mint an HS256 access token the backend accepts as a logged-in user —
 * bypassing Steam OAuth (which cannot be scripted). Mirrors the backend's
 * AccessTokenGenerator claims: sub (UserId), steam_id, role; iss=skinora,
 * aud=skinora-client. The secret must match the e2e stack's Jwt__Secret.
 */
export function mintAccessToken(opts: {
  userId: string;
  steamId: string;
  role?: 'user' | 'admin' | 'super_admin';
}): string {
  return jwt.sign({ steam_id: opts.steamId, role: opts.role ?? 'user' }, e2eConfig.jwtSecret, {
    algorithm: 'HS256',
    issuer: e2eConfig.jwtIssuer,
    audience: e2eConfig.jwtAudience,
    subject: opts.userId,
    expiresIn: '60m',
  });
}
