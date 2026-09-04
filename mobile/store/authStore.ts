import { create } from 'zustand';
import { api } from '../services/endpoints';
import { configureAuth, ApiError, type AuthTokens } from '../services/apiClient';
import { clearSession, readSession, saveSession } from '../services/secureStorage';
import type { AuthenticatedUser } from '../types/api';

interface AuthState {
  status: 'loading' | 'authenticated' | 'anonymous';
  user: AuthenticatedUser | null;
  tokens: AuthTokens | null;
  error: string | null;
  restore: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => Promise<void>;
  clearError: () => void;
}

/**
 * Session state. Tokens are mirrored into SecureStore so a cold start does not
 * ask the user to sign in again, and the API client is given a refresh callback
 * so an expired access token is invisible to the screens.
 */
export const useAuthStore = create<AuthState>((set, get) => ({
  status: 'loading',
  user: null,
  tokens: null,
  error: null,

  restore: async () => {
    const stored = await readSession();

    if (!stored) {
      set({ status: 'anonymous', user: null, tokens: null });
      return;
    }

    try {
      const user = JSON.parse(stored.user) as AuthenticatedUser;
      set({
        status: 'authenticated',
        user,
        tokens: { accessToken: stored.accessToken, refreshToken: stored.refreshToken },
      });
    } catch {
      await clearSession();
      set({ status: 'anonymous', user: null, tokens: null });
    }
  },

  login: async (email, password) => {
    set({ error: null });
    try {
      const result = await api.auth.login(email.trim(), password);
      await persist(result, set);
    } catch (error) {
      set({ error: messageOf(error) });
      throw error;
    }
  },

  register: async (email, password, displayName) => {
    set({ error: null });
    try {
      const result = await api.auth.register(email.trim(), password, displayName.trim());
      await persist(result, set);
    } catch (error) {
      set({ error: messageOf(error) });
      throw error;
    }
  },

  logout: async () => {
    const tokens = get().tokens;
    if (tokens) {
      // Best effort: a failed revocation must not trap the user in a session.
      await api.auth.logout(tokens.refreshToken).catch(() => undefined);
    }

    await clearSession();
    set({ status: 'anonymous', user: null, tokens: null, error: null });
  },

  clearError: () => set({ error: null }),
}));

type Setter = (partial: Partial<AuthState>) => void;

async function persist(
  result: { accessToken: string; refreshToken: string; user: AuthenticatedUser },
  set: Setter,
): Promise<void> {
  await saveSession({
    accessToken: result.accessToken,
    refreshToken: result.refreshToken,
    user: JSON.stringify(result.user),
  });

  set({
    status: 'authenticated',
    user: result.user,
    tokens: { accessToken: result.accessToken, refreshToken: result.refreshToken },
    error: null,
  });
}

function messageOf(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }

  return 'No pudimos completar la operación. Inténtalo de nuevo.';
}

// Wire the API client once, at module load.
configureAuth(
  () => useAuthStore.getState().tokens,
  async () => {
    const current = useAuthStore.getState().tokens;
    if (!current) {
      return null;
    }

    try {
      const refreshed = await api.auth.refresh(current.refreshToken);
      await saveSession({
        accessToken: refreshed.accessToken,
        refreshToken: refreshed.refreshToken,
        user: JSON.stringify(refreshed.user),
      });

      const tokens = { accessToken: refreshed.accessToken, refreshToken: refreshed.refreshToken };
      useAuthStore.setState({ status: 'authenticated', user: refreshed.user, tokens });
      return tokens;
    } catch {
      await clearSession();
      useAuthStore.setState({ status: 'anonymous', user: null, tokens: null });
      return null;
    }
  },
);
