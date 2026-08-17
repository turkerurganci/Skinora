/**
 * E2E harness constants. Defaults match docker-compose.e2e.yml (fixed test
 * values — NOT secrets). Override via env when running against a different
 * stack/ports.
 */
export const e2eConfig = {
  // nginx origin — serves the frontend and proxies /api/v1 + /hubs to backend.
  baseUrl: process.env.E2E_BASE_URL || 'http://localhost:8080',
  // Fake sidecar control surface (/__e2e/payment/*). Published from compose.
  fakeUrl: process.env.E2E_FAKE_URL || 'http://localhost:5200',

  // JWT-inject login — must equal the backend's Jwt__Secret in the e2e stack.
  jwtSecret: process.env.E2E_JWT_SECRET || 'e2e-jwt-secret-do-not-use-in-prod-0123456789',
  jwtIssuer: 'skinora',
  jwtAudience: 'skinora-client',

  // SQL Server (published e2e port) — seed + assertions.
  db: {
    server: process.env.E2E_DB_SERVER || 'localhost',
    port: parseInt(process.env.E2E_DB_PORT || '14333', 10),
    user: process.env.E2E_DB_USER || 'sa',
    password: process.env.E2E_DB_PASSWORD || 'E2e!Strong!Pass!2345',
    database: process.env.E2E_DB_NAME || 'Skinora',
  },

  // T137a — `botSteamId` was removed: the harness no longer seeds a platform
  // bot (T117 dropped PlatformSteamBots). The fake sidecar still carries a
  // FAKE_BOT_STEAM_ID identity for its custody-era trade routes; retiring that
  // is T137's scope, not the harness's.
};
