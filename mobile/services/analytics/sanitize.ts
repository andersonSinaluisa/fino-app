import {
  EVENT_SCHEMA,
  NUMERIC_KEYS,
  NUMERIC_MAX,
  NUMERIC_MIN,
  SUSPICIOUS_KEY_PATTERN,
  SUSPICIOUS_PATTERN_EXCEPTIONS,
} from './schema';
import type { AnalyticsEventName } from './events';

export type AnalyticsValue = string | number | boolean | null | undefined;
export type AnalyticsProperties = Record<string, AnalyticsValue>;

/** Propiedades ya limpias, listas para el proveedor. */
export type SafeProperties = Record<string, string | number | boolean>;

export interface SanitizeResult {
  properties: SafeProperties;
  /** Claves descartadas y por qué. Se avisa en desarrollo, se cuenta en producción. */
  dropped: { key: string; reason: DropReason }[];
}

export type DropReason =
  | 'not_in_allowlist'
  | 'suspicious_key'
  | 'number_not_allowed_here'
  | 'number_out_of_range'
  | 'string_looks_like_content'
  | 'unsupported_type';

/**
 * Forma admitida para un valor de texto: un símbolo de vocabulario cerrado.
 *
 * Esta es la segunda mitad de la protección, y la que de verdad ataja un
 * accidente. La allowlist controla QUÉ CLAVES salen; esto controla QUÉ FORMA
 * puede tener el valor. Una descripción de movimiento ("Supermaxi Urdesa"),
 * un nombre ("María Pérez") o una transcripción de voz llevan espacios,
 * tildes o mayúsculas mezcladas y no pasan este filtro aunque alguien las
 * ponga en una clave permitida.
 */
const SAFE_TOKEN_PATTERN = /^[a-z0-9_.:-]+$/;
const MAX_TOKEN_LENGTH = 40;

/**
 * Filtra las propiedades de un evento contra su esquema.
 *
 * Nunca lanza: analytics jamás puede tumbar un flujo de producto. Lo que no
 * cumple, se cae.
 */
export function sanitizeAnalyticsProperties(
  event: AnalyticsEventName,
  properties: AnalyticsProperties | undefined,
): SanitizeResult {
  const allowed = EVENT_SCHEMA[event];
  const result: SafeProperties = {};
  const dropped: { key: string; reason: DropReason }[] = [];

  if (!properties || !allowed) {
    return { properties: result, dropped };
  }

  for (const [key, value] of Object.entries(properties)) {
    // Un valor ausente no es un error: simplemente no se manda.
    if (value === null || value === undefined) {
      continue;
    }

    if (!allowed.includes(key)) {
      dropped.push({ key, reason: 'not_in_allowlist' });
      continue;
    }

    if (!SUSPICIOUS_PATTERN_EXCEPTIONS.has(key) && SUSPICIOUS_KEY_PATTERN.test(key)) {
      dropped.push({ key, reason: 'suspicious_key' });
      continue;
    }

    if (typeof value === 'boolean') {
      result[key] = value;
      continue;
    }

    if (typeof value === 'number') {
      if (!NUMERIC_KEYS.has(key)) {
        dropped.push({ key, reason: 'number_not_allowed_here' });
        continue;
      }

      if (!Number.isInteger(value) || value < NUMERIC_MIN || value > NUMERIC_MAX) {
        dropped.push({ key, reason: 'number_out_of_range' });
        continue;
      }

      result[key] = value;
      continue;
    }

    if (typeof value === 'string') {
      const normalized = value.trim();

      if (normalized.length === 0) {
        continue;
      }

      if (normalized.length > MAX_TOKEN_LENGTH || !SAFE_TOKEN_PATTERN.test(normalized.toLowerCase())) {
        dropped.push({ key, reason: 'string_looks_like_content' });
        continue;
      }

      result[key] = normalized.toLowerCase();
      continue;
    }

    dropped.push({ key, reason: 'unsupported_type' });
  }

  return { properties: result, dropped };
}
