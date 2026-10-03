import { create } from 'zustand';
import { api } from '../services/endpoints';
import { configureAuth, ApiError, type AuthTokens } from '../services/apiClient';
import { clearSession, readSession, saveSession } from '../services/secureStorage';
import { useDeviceStore } from './deviceStore';
import { AnalyticsEvent, ErrorReason, analytics, track } from '../services/analytics';
import type { AuthenticatedUser, OnboardingStatus, RegisterConsents } from '../types/api';

interface AuthState {
  status: 'loading' | 'authenticated' | 'anonymous';
  user: AuthenticatedUser | null;
  tokens: AuthTokens | null;
  error: string | null;
  restore: () => Promise<void>;
  /**
   * Entregable 22 ("Privacidad completa"): resolves to true when this login
   * undid a pending "eliminar mi cuenta" request still inside its grace
   * period, so the screen can tell the person their account is active again.
   */
  login: (email: string, password: string) => Promise<boolean>;
  /** consents: lo que la persona marcó (18+ y términos obligatorios; datos de uso opcional). */
  register: (email: string, password: string, displayName: string, consents: RegisterConsents) => Promise<void>;
  logout: () => Promise<void>;
  /** Entregable 19: same local cleanup as logout(), plus revokes every other device too. */
  logoutAllDevices: () => Promise<void>;
  clearError: () => void;
  /**
   * Onboarding funcional: aplica el snapshot que devuelve cualquier llamada a
   * /api/v1/onboarding/* al usuario en memoria Y a la sesión persistida, para
   * que un cierre de la app a medio onboarding retome exactamente donde se
   * quedó sin depender de una llamada extra a /auth/refresh.
   */
  setOnboarding: (onboarding: OnboardingStatus) => void;
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
    track(AnalyticsEvent.LoginStarted);
    try {
      const result = await api.auth.login(email.trim(), password);
      await persist(result, set);
      track(AnalyticsEvent.LoginSucceeded);
      return result.accountDeletionCancelled;
    } catch (error) {
      // §19: solo la razón, de un conjunto cerrado. El mensaje del servidor
      // puede incluir el correo que se intentó y no tiene por qué salir.
      track(AnalyticsEvent.LoginFailed, { reason: authFailureReason(error) });
      set({ error: messageOf(error) });
      throw error;
    }
  },

  register: async (email, password, displayName, consents) => {
    set({ error: null });
    track(AnalyticsEvent.SignupStarted);
    try {
      const result = await api.auth.register(email.trim(), password, displayName.trim(), consents);
      await persist(result, set);
      // Datos de uso solo si la persona marcó la casilla (LOPDP art. 8).
      await analytics.setConsent(consents.analyticsConsent ? 'granted' : 'denied');

      // §4: el dispositivo ya venía generando eventos anónimos (instalación,
      // pantalla de registro). `alias` los cose al id interno para no perder
      // el tramo install -> signup del embudo de activación.
      analytics.aliasToCurrentUser(result.user.id);
      track(AnalyticsEvent.SignupSucceeded);
    } catch (error) {
      track(AnalyticsEvent.SignupFailed, { reason: authFailureReason(error) });
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

    await finishLocalLogout(set);
  },

  logoutAllDevices: async () => {
    // Best effort, same reasoning as logout(): the person is leaving either way.
    await api.auth.logoutAll().catch(() => undefined);
    await finishLocalLogout(set);
  },

  clearError: () => set({ error: null }),

  setOnboarding: (onboarding) => {
    const current = get().user;
    if (!current) {
      return;
    }

    const updated: AuthenticatedUser = { ...current, onboarding };
    set({ user: updated });

    const tokens = get().tokens;
    if (tokens) {
      void saveSession({
        accessToken: tokens.accessToken,
        refreshToken: tokens.refreshToken,
        user: JSON.stringify(updated),
      }).catch(() => undefined);
    }
  },
}));

/**
 * The part of signing out that has nothing to do with which backend call got
 * there first: stop this device's push, forget the cached push preferences,
 * wipe SecureStore, and flip to anonymous. Shared by logout() (revokes one
 * session) and logoutAllDevices() (revokes every session, including this
 * one -- by the time this runs the server side is already done).
 */
async function finishLocalLogout(set: Setter): Promise<void> {
  // Entregable 18: on a shared device, a former session must stop receiving
  // push. Also best effort -- the token may already be gone server-side, or
  // there may be no token yet if push registration never finished.
  const expoPushToken = useDeviceStore.getState().expoPushToken;
  if (expoPushToken) {
    await api.notifications.unregisterDevice(expoPushToken).catch(() => undefined);
  }
  useDeviceStore.getState().reset();

  // §3: cortar el hilo ANTES de limpiar la sesión. En un teléfono compartido,
  // los eventos de quien entre después no pueden quedar colgando del usuario
  // que acaba de salir.
  track(AnalyticsEvent.Logout);
  analytics.reset();

  await clearSession();
  set({ status: 'anonymous', user: null, tokens: null, error: null });
}

/**
 * §19: por qué falló una autenticación, en un conjunto cerrado.
 *
 * El mensaje del backend puede repetir el correo que se intentó y no tiene
 * por qué salir de aquí; el código de estado responde igual de bien "¿la
 * gente falla por credenciales o porque no hay red?".
 */
function authFailureReason(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return ErrorReason.Unknown;
  }

  if (error.status === 0) {
    return ErrorReason.NetworkError;
  }

  if (error.status === 401 || error.status === 403) {
    return ErrorReason.PermissionDenied;
  }

  if (error.status >= 500) {
    return ErrorReason.ServerError;
  }

  return ErrorReason.Unknown;
}

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
    } catch (error) {
      // BUG (reported: "cuando el teléfono pierde conexión y la recupera,
      // pide iniciar sesión"): a dropped connection right as the app tries
      // to refresh is a *network* failure (ApiError status 0 -- see
      // apiClient.ts's networkProblem/timeout handling), not proof the
      // refresh token is invalid. The old code wiped SecureStore on ANY
      // failure here, so a plain connectivity hiccup logged the user out
      // even though their 30-day refresh token was still perfectly good.
      // Only a genuine rejection *from the server* means the session is
      // really over: 401 (token missing/expired/revoked/reused) or 403
      // (account no longer active) -- see AuthService.RefreshAsync, the
      // only two exceptions it ever throws. Everything else (no response,
      // timeout, a transient 5xx) leaves the stored session untouched so
      // the next request -- once connectivity is actually back -- simply
      // tries the refresh again instead of forcing a fresh login.
      const isRealRejection = error instanceof ApiError && (error.status === 401 || error.status === 403);
      if (isRealRejection) {
        await clearSession();
        useAuthStore.setState({ status: 'anonymous', user: null, tokens: null });
      }
      return null;
    }
  },
);
