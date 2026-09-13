import Constants from 'expo-constants';
import { Platform } from 'react-native';

export interface AnalyticsConfig {
  apiKey: string | null;
  host: string;
  /** 'development' | 'preview' | 'production' */
  environment: string;
  appVersion: string;
  buildNumber: string;
  platform: string;
  locale: string;
}

/**
 * La configuración viene de variables EXPO_PUBLIC_*, igual que la URL de la
 * API (ver services/config.ts). Sin `EXPO_PUBLIC_POSTHOG_KEY` no se instancia
 * ningún proveedor real y la app se queda en debug/noop: no hay forma de que
 * un build empiece a mandar eventos sin que alguien haya puesto la clave a
 * propósito.
 */
export function resolveAnalyticsConfig(): AnalyticsConfig {
  const expoConfig = Constants.expoConfig;
  const ios = expoConfig?.ios;
  const android = expoConfig?.android;

  return {
    apiKey: process.env.EXPO_PUBLIC_POSTHOG_KEY || null,
    host: process.env.EXPO_PUBLIC_POSTHOG_HOST || 'https://us.i.posthog.com',
    environment: process.env.EXPO_PUBLIC_ENVIRONMENT || (__DEV__ ? 'development' : 'production'),
    appVersion: expoConfig?.version ?? 'unknown',
    buildNumber: String(ios?.buildNumber ?? android?.versionCode ?? 'unknown'),
    platform: Platform.OS,
    locale: normalizeLocale(expoConfig?.locales ? Object.keys(expoConfig.locales)[0] : undefined),
  };
}

/**
 * `es-EC` -> `es-ec`. Se queda en idioma y región: ninguna de las dos cosas
 * identifica a nadie y las dos sirven para segmentar.
 */
function normalizeLocale(locale: string | undefined): string {
  if (!locale) return 'es-ec';
  return locale.toLowerCase().replace('_', '-');
}
