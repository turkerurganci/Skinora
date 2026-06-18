import { apiClient } from "./client";

/**
 * Response body for A5 — POST /auth/steam/re-verify (07 §4.7).
 *
 * Caller redirects the browser to `steamAuthUrl`; Steam authenticates the
 * user and redirects back to the backend callback (A6), which in turn
 * redirects to `returnUrl` with `?reAuthToken=<token>` appended.
 *
 * The token is single-use (Redis GETDEL, 5 min TTL) and is bound to the
 * current user — it is consumed on the next wallet update call and must
 * not be reused. Browser history will retain the URL so the page should
 * strip the param after capture (router.replace).
 */
export interface ReVerifyInitiateResponse {
  steamAuthUrl: string;
}

export function initiateSteamReVerify(
  purpose: string,
  returnUrl: string,
): Promise<ReVerifyInitiateResponse> {
  return apiClient<ReVerifyInitiateResponse>("/auth/steam/re-verify", {
    method: "POST",
    body: JSON.stringify({ purpose, returnUrl }),
  });
}

/**
 * A4 — `GET /auth/me` current-session profile (07 §4.5). Mirrors the backend
 * `CurrentUserDto`. `isSuspended` (T105a) drives the restricted session
 * (SuspendedHeader + S03d).
 */
export interface MeResponse {
  id: string;
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  mobileAuthenticatorActive: boolean;
  tosAccepted: boolean;
  /**
   * Accepted ToS version (07 §4.5, WP11). `null` until first acceptance. The
   * client compares this against the current ToS version
   * (`NEXT_PUBLIC_TOS_VERSION`) to decide whether to re-prompt on a version bump
   * (T30) — see {@link TosRepromptGate}.
   */
  tosAcceptedVersion: string | null;
  role: string;
  language: string;
  hasSellerWallet: boolean;
  hasRefundWallet: boolean;
  createdAt: string;
  isSuspended: boolean;
}

export function getMe(): Promise<MeResponse> {
  return apiClient<MeResponse>("/auth/me");
}

/**
 * Response body for A3 — POST /auth/tos/accept (07 §4.4).
 */
export interface AcceptTosResponse {
  accepted: boolean;
  acceptedAt: string;
}

/**
 * A3 — `POST /auth/tos/accept` (07 §4.4, WP11). Captures ToS acceptance + 18+
 * self-attestation for the given version. Re-accepting the SAME version yields
 * a 409 `TOS_ALREADY_ACCEPTED` (callers treat that as already-satisfied); a new
 * (bumped) version is accepted as a re-acceptance.
 */
export function acceptTos(tosVersion: string): Promise<AcceptTosResponse> {
  return apiClient<AcceptTosResponse>("/auth/tos/accept", {
    method: "POST",
    body: JSON.stringify({ tosVersion, ageOver18: true }),
  });
}
