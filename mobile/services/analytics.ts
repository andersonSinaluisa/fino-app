/**
 * Fachada pública de analytics.
 *
 * Toda la app importa desde aquí. Este archivo existía antes con un `track()`
 * que solo escribía en consola y un comentario diciendo que faltaba conectar
 * un proveedor real; ahora delega en `AnalyticsService` (services/analytics/),
 * que aplica la allowlist y habla con el proveedor. Los sitios que ya
 * llamaban a `track()` siguen funcionando sin cambios de import.
 *
 * Lo que NO hay que hacer nunca:
 *   - llamar al SDK del proveedor directamente desde una pantalla;
 *   - inventar el nombre de un evento (el catálogo es `AnalyticsEvent`);
 *   - pasar el objeto de datos completo "por si acaso" -- solo lo que el
 *     esquema del evento permite.
 */
import { analytics } from './analytics/service';
import type { AnalyticsEventName, AnalyticsScreenName } from './analytics/events';
import type { AnalyticsProperties } from './analytics/sanitize';

export {
  AnalysisResult,
  AnalyticsEvent,
  AnalyticsScreen,
  AnalyticsSource,
  ANALYTICS_SCHEMA_VERSION,
  BudgetFlow,
  BudgetPeriodValue,
  CategorySource,
  CountBucket,
  DurationBucket,
  EntryMode,
  ErrorReason,
  FileFormat,
  SourceCountBucket,
  TransactionDirection,
  ValueSource,
} from './analytics/events';

export type {
  AnalysisResultValue,
  AnalyticsEventName,
  AnalyticsScreenName,
  AnalyticsSourceValue,
  CountBucketValue,
  DurationBucketValue,
  EntryModeValue,
  ErrorReasonValue,
  FileFormatValue,
} from './analytics/events';

export {
  toAnalyticsSymbol,
  toCountBucket,
  toDurationBucket,
  toFileFormat,
  toSourceCountBucket,
  toTranscriptLengthBucket,
} from './analytics/buckets';

export type { AnalyticsProperties } from './analytics/sanitize';
export { AnalyticsService, analytics } from './analytics/service';
export type { ConsentState } from './analytics/storage';

/**
 * Cómo llegó el movimiento. Alias histórico de `EntryModeValue`: se mantiene
 * porque QuickCashEntrySheet lo usa como tipo de sus props, y renombrarlo no
 * aportaría nada.
 */
export type QuickEntryMethod = 'keypad' | 'smart_text' | 'voice' | 'frequent' | 'recent' | 'full_form';

/** Compatibilidad: el tipo del catálogo, con el nombre que usaba este módulo. */
export type AnalyticsEvent_ = AnalyticsEventName;

export function track(event: AnalyticsEventName, properties?: AnalyticsProperties): void {
  analytics.track(event, properties);
}

/** §30: como mucho una vez por usuario, persistido. */
export function trackOnce(event: AnalyticsEventName, properties?: AnalyticsProperties): Promise<boolean> {
  return analytics.trackOnce(event, properties);
}

export function screen(name: AnalyticsScreenName, properties?: AnalyticsProperties): void {
  analytics.screen(name, properties);
}

export function identify(analyticsId: string, properties?: AnalyticsProperties): void {
  analytics.identify(analyticsId, properties);
}

export function setUserProperties(properties: AnalyticsProperties): void {
  analytics.setUserProperties(properties);
}

export function resetAnalytics(): void {
  analytics.reset();
}
