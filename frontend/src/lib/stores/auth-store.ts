import { create } from "zustand";

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
  setAccessToken: (token) => set({ accessToken: token, isAuthenticated: !!token }),
  setProfile: (profile) => set((state) => ({ ...state, ...profile })),
  logout: () =>
    set({
      accessToken: null,
      isAuthenticated: false,
      isAdmin: false,
      isSuspended: false,
      displayName: null,
      avatarUrl: null,
    }),
}));
