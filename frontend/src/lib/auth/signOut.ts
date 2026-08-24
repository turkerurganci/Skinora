import { logout as logoutRequest } from "@/lib/api/auth";
import { useAuthStore } from "@/lib/stores/auth-store";

/**
 * Ends the session on BOTH sides: revokes the refresh token server-side (A8)
 * and clears the local access token.
 *
 * F7a — every sign-out path must go through here. The store's `logout()` only
 * drops the localStorage token; on its own it leaves the HttpOnly refresh
 * cookie valid for its full lifetime, so the session was never actually
 * terminated (`Session-LogoutDoesNotRevokeRefreshToken`).
 *
 * The server call is BEST-EFFORT and its failure is swallowed on purpose: if
 * the network is down, the token already expired, or the rate limiter rejects
 * the call, the user must still end up signed out locally. Refusing to clear
 * local state on a failed revoke would strand them in a session they asked to
 * leave — a worse outcome than a refresh token that expires on its own.
 *
 * `useAuthStore.getState().logout()` is used rather than a hook so this is
 * callable from event handlers and non-React code alike.
 */
export async function signOut(): Promise<void> {
  try {
    await logoutRequest();
  } catch {
    // Best-effort — see above.
  } finally {
    useAuthStore.getState().logout();
  }
}
