import Constants from 'expo-constants';
import { Platform } from 'react-native';

interface RuntimeConstants {
  expoConfig?: {
    extra?: { apiBaseUrl?: string };
    hostUri?: string;
  };
  linkingUri?: string;
}

/**
 * Where the API lives.
 *
 * Order: EXPO_PUBLIC_API_URL (set in .env or the EAS build profile), then the
 * `apiBaseUrl` in app.json, then a platform-appropriate localhost. A physical
 * iPhone running Expo Go cannot reach the dev machine through `localhost`, so
 * in that case we reuse Metro's LAN host and keep the API port.
 */
export function resolveBaseUrlForRuntime(
  runtime: RuntimeConstants,
  platform: string,
  envApiUrl?: string,
): string {
  const fromEnv = envApiUrl;
  if (fromEnv && fromEnv.length > 0) {
    return fromEnv.replace(/\/+$/, '');
  }

  const extra = runtime.expoConfig?.extra;
  const configured = extra?.apiBaseUrl;

  if (configured) {
    const normalized = configured.replace(/\/+$/, '');
    const local = parseLocalApiUrl(normalized);

    if (!local) {
      return normalized;
    }

    if (platform === 'android') {
      return `${local.protocol}//10.0.2.2:${local.port}`;
    }

    if (platform === 'ios') {
      const lanHost = metroHost(runtime);
      if (lanHost) {
        return `${local.protocol}//${lanHost}:${local.port}`;
      }
    }

    return normalized;
  }

  return platform === 'android' ? 'http://10.0.2.2:5080' : 'http://localhost:5080';
}

function resolveBaseUrl(): string {
  return resolveBaseUrlForRuntime(
    Constants as RuntimeConstants,
    Platform.OS,
    process.env.EXPO_PUBLIC_API_URL,
  );
}

function parseLocalApiUrl(value: string): { protocol: string; port: string } | null {
  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    if (host !== 'localhost' && host !== '127.0.0.1' && host !== '::1') {
      return null;
    }

    return { protocol: url.protocol, port: url.port || (url.protocol === 'https:' ? '443' : '80') };
  } catch {
    return null;
  }
}

function metroHost(runtime: RuntimeConstants): string | null {
  const candidates = [runtime.expoConfig?.hostUri, runtime.linkingUri];

  for (const candidate of candidates) {
    if (!candidate) {
      continue;
    }

    const host = candidate
      .replace(/^[a-z][a-z0-9+.-]*:\/\//i, '')
      .replace(/^\/\//, '')
      .split('/')[0]
      ?.split(':')[0];

    if (host && host !== 'localhost' && host !== '127.0.0.1') {
      return host;
    }
  }

  return null;
}

export const API_BASE_URL = resolveBaseUrl();

export const config = {
  apiBaseUrl: API_BASE_URL,
  realtimeUrl: `${API_BASE_URL}/hubs/nexo`,
  requestTimeoutMs: 20000,
} as const;
