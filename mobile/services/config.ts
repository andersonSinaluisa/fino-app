import Constants from 'expo-constants';
import { Platform } from 'react-native';

/**
 * Where the API lives.
 *
 * Order: EXPO_PUBLIC_API_URL (set in .env or the EAS build profile), then the
 * `apiBaseUrl` in app.json, then a platform-appropriate localhost. The Android
 * emulator reaches the host machine at 10.0.2.2, which is the single most common
 * "why can't the app see my backend" in a React Native project.
 */
function resolveBaseUrl(): string {
  const fromEnv = process.env.EXPO_PUBLIC_API_URL;
  if (fromEnv && fromEnv.length > 0) {
    return fromEnv.replace(/\/+$/, '');
  }

  const extra = Constants.expoConfig?.extra as { apiBaseUrl?: string } | undefined;
  const configured = extra?.apiBaseUrl;

  if (configured && !configured.includes('localhost')) {
    return configured.replace(/\/+$/, '');
  }

  const port = configured?.split(':').pop() ?? '5080';
  return Platform.OS === 'android' ? `http://10.0.2.2:${port}` : `http://localhost:${port}`;
}

export const API_BASE_URL = resolveBaseUrl();

export const config = {
  apiBaseUrl: API_BASE_URL,
  realtimeUrl: `${API_BASE_URL}/hubs/nexo`,
  requestTimeoutMs: 20000,
} as const;
