import { create } from "zustand";

/**
 * localStorage key holding the JWT access token. The API client
 * ({@link "@/lib/api/client"}) reads this key on every request, so the store is
 * the single writer: {@link AuthState.setAccessToken} persists/clears it and
 * {@link AuthState.logout} removes it (WP11 — previously the callback never
 * wrote it, so `isAuthenticated` was permanently false).
 */
export const ACCESS_TOKEN_STORAGE_KEY = "access_token";

function persistAccessToken(token: string | null): void {
  if (typeof window === "undefined") return;
  if (token) {
    window.localStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, token);
  } else {
    window.localStorage.removeItem(ACCESS_TOKEN_STORAGE_KEY);
  }
}

interface AuthState {
  isAuthenticated: boolean;
  accessToken: string | null;
  isAdmin: boolean;
  isSuspended: boolean;
  displayName: string | null;
  avatarUrl: string | null;
  setAccessToken: (token: string | null) => void;
  setProfile: (profile: {
    isAdmin?: boolean;
    isSuspended?: boolean;
    displayName?: string | null;
    avatarUrl?: string | null;
  }) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  accessToken: null,
  isAdmin: false,
  isSuspended: false,
  displayName: null,
  avatarUrl: null,
  setAccessToken: (token) => {
    persistAccessToken(token);
    set({ accessToken: token, isAuthenticated: !!token });
  },
  setProfile: (profile) => set((state) => ({ ...state, ...profile })),
  logout: () => {
    persistAccessToken(null);
    set({
      accessToken: null,
      isAuthenticated: false,
      isAdmin: false,
      isSuspended: false,
      displayName: null,
      avatarUrl: null,
    });
  },
}));
