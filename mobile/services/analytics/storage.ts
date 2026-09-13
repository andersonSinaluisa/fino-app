import AsyncStorage from '@react-native-async-storage/async-storage';

const ONCE_PREFIX = 'fino.analytics.once';
const LAST_ACTIVE_KEY = 'fino.analytics.lastActiveAt';
const CONSENT_KEY = 'fino.analytics.consent';
const ENABLED_KEY = 'fino.analytics.enabled';

/** §24: tres estados, no un booleano. "Todavía no preguntamos" no es "dijo que no". */
export type ConsentState = 'unknown' | 'granted' | 'denied';

/**
 * §30: los hitos únicos se guardan por USUARIO, no por dispositivo.
 *
 * Si la clave fuera solo el evento, alguien que entra con otra cuenta en el
 * mismo teléfono nunca dispararía su `first_value_reached` y el embudo de
 * activación mediría de menos. Con el id opaco dentro de la clave, cada
 * usuario tiene su propio registro.
 */
function onceKey(analyticsId: string, event: string): string {
  return `${ONCE_PREFIX}.${analyticsId}.${event}`;
}

/**
 * Devuelve true la PRIMERA vez que se pregunta por ese (usuario, evento) y
 * false siempre después. Escribe la marca antes de que el llamador emita el
 * evento, así que dos llamadas en paralelo no lo duplican.
 *
 * Ante un fallo de almacenamiento devuelve false: preferimos perder un hito
 * a inflar la métrica de activación con duplicados.
 */
export async function claimOnce(analyticsId: string, event: string): Promise<boolean> {
  const key = onceKey(analyticsId, event);

  try {
    const existing = await AsyncStorage.getItem(key);
    if (existing) {
      return false;
    }

    await AsyncStorage.setItem(key, new Date().toISOString());
    return true;
  } catch {
    return false;
  }
}

/** Solo para tests y para el borrado de datos del usuario. */
export async function clearOnce(analyticsId: string, event: string): Promise<void> {
  try {
    await AsyncStorage.removeItem(onceKey(analyticsId, event));
  } catch {
    // Ignorado a propósito.
  }
}

export async function readLastActiveAt(): Promise<number | null> {
  try {
    const raw = await AsyncStorage.getItem(LAST_ACTIVE_KEY);
    if (!raw) return null;

    const parsed = Number.parseInt(raw, 10);
    return Number.isFinite(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

export async function writeLastActiveAt(timestamp: number): Promise<void> {
  try {
    await AsyncStorage.setItem(LAST_ACTIVE_KEY, String(timestamp));
  } catch {
    // Ignorado a propósito.
  }
}

export async function readConsent(): Promise<ConsentState> {
  try {
    const raw = await AsyncStorage.getItem(CONSENT_KEY);
    return raw === 'granted' || raw === 'denied' ? raw : 'unknown';
  } catch {
    return 'unknown';
  }
}

export async function writeConsent(state: ConsentState): Promise<void> {
  try {
    await AsyncStorage.setItem(CONSENT_KEY, state);
  } catch {
    // Ignorado a propósito.
  }
}

/** §23: el interruptor explícito del usuario, independiente del consentimiento. */
export async function readEnabled(): Promise<boolean> {
  try {
    const raw = await AsyncStorage.getItem(ENABLED_KEY);
    return raw !== 'false';
  } catch {
    return true;
  }
}

export async function writeEnabled(enabled: boolean): Promise<void> {
  try {
    await AsyncStorage.setItem(ENABLED_KEY, enabled ? 'true' : 'false');
  } catch {
    // Ignorado a propósito.
  }
}
