import * as SecureStore from 'expo-secure-store';

/**
 * Tokens live in the platform keystore/keychain, never in AsyncStorage.
 * On the rare device where SecureStore is unavailable we fail closed: the user
 * signs in again rather than having credentials written somewhere weaker.
 */
const ACCESS_TOKEN_KEY = 'fino.accessToken';
const REFRESH_TOKEN_KEY = 'fino.refreshToken';
const USER_KEY = 'fino.user';

export interface StoredSession {
  accessToken: string;
  refreshToken: string;
  user: string;
}

export async function saveSession(session: StoredSession): Promise<void> {
  await Promise.all([
    SecureStore.setItemAsync(ACCESS_TOKEN_KEY, session.accessToken),
    SecureStore.setItemAsync(REFRESH_TOKEN_KEY, session.refreshToken),
    SecureStore.setItemAsync(USER_KEY, session.user),
  ]);
}

export async function readSession(): Promise<StoredSession | null> {
  try {
    const [accessToken, refreshToken, user] = await Promise.all([
      SecureStore.getItemAsync(ACCESS_TOKEN_KEY),
      SecureStore.getItemAsync(REFRESH_TOKEN_KEY),
      SecureStore.getItemAsync(USER_KEY),
    ]);

    if (!accessToken || !refreshToken || !user) {
      return null;
    }

    return { accessToken, refreshToken, user };
  } catch {
    return null;
  }
}

export async function clearSession(): Promise<void> {
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY),
    SecureStore.deleteItemAsync(USER_KEY),
  ]);
}
