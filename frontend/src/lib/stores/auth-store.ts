import { create } from "zustand";

interface AuthState {
  isAuthenticated: boolean;
  accessToken: string | null;
  setAccessToken: (token: string | null) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  isAuthenticated: false,
  accessToken: null,
  setAccessToken: (token) =>
    set({ accessToken: token, isAuthenticated: !!token }),
  logout: () => set({ accessToken: null, isAuthenticated: false }),
}));
