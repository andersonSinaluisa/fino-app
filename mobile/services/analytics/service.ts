import { ANALYTICS_SCHEMA_VERSION, type AnalyticsEventName, type AnalyticsScreenName } from './events';
import { sanitizeAnalyticsProperties, type AnalyticsProperties, type SafeProperties } from './sanitize';
import type { AnalyticsProvider } from './providers/types';
import { NoopAnalyticsProvider } from './providers/noop';
import { DebugAnalyticsProvider } from './providers/debug';
import { PostHogAnalyticsProvider } from './providers/posthog';
import { resolveAnalyticsConfig, type AnalyticsConfig } from './config';
import {
  claimOnce,
  readConsent,
  readEnabled,
  readLastActiveAt,
  writeConsent,
  writeEnabled,
  writeLastActiveAt,
  type ConsentState,
} from './storage';

/**
 * §14: qué tan larga puede ser una pausa sin que cuente como sesión nueva.
 * 30 minutos es lo habitual en product analytics y es lo que asumen los
 * cálculos de retención del proveedor.
 */
const SESSION_TIMEOUT_MS = 30 * 60 * 1000;

/** Eventos emitidos antes de que `init()` termine. Acotado a propósito. */
const MAX_PENDING_EVENTS = 50;

interface PendingEvent {
  kind: 'track' | 'screen';
  name: string;
  properties: SafeProperties;
}

export interface AnalyticsServiceOptions {
  /** Inyectable en tests. Si no se pasa, se elige según config y consentimiento. */
  provider?: AnalyticsProvider;
  config?: AnalyticsConfig;
  /**
   * §24: si la jurisdicción exige consentimiento previo, ponerlo en true y el
   * estado 'unknown' deja de enviar. La app real lo usa en true (LOPDP art. 8):
   * la persona lo da al registrarse, al aceptar una versión nueva de los
   * documentos legales o en Perfil → Privacidad. El valor por defecto de la
   * clase sigue en false solo para los tests que la construyen.
   */
  requireExplicitConsent?: boolean;
}

/**
 * La única puerta de salida de analytics de FINO.
 *
 * Todo el producto llama aquí. Nadie llama al SDK. Eso es lo que permite
 * cambiar de proveedor, apagarlo entero, o auditar de una sola lectura qué
 * información sale de la app -- que es la pregunta que una app de finanzas
 * tiene que poder responder.
 */
export class AnalyticsService {
  private provider: AnalyticsProvider = new NoopAnalyticsProvider();
  private config: AnalyticsConfig | null = null;
  private initialized = false;
  private consent: ConsentState = 'unknown';
  private enabled = true;
  private requireExplicitConsent: boolean;
  private analyticsId: string | null = null;
  private pending: PendingEvent[] = [];
  private droppedCount = 0;

  constructor(private readonly options: AnalyticsServiceOptions = {}) {
    this.requireExplicitConsent = options.requireExplicitConsent ?? false;
  }

  /** Cuántas propiedades ha descartado el sanitizador. Para tests y diagnóstico. */
  get droppedPropertyCount(): number {
    return this.droppedCount;
  }

  get providerName(): string {
    return this.provider.name;
  }

  async init(): Promise<void> {
    if (this.initialized) {
      return;
    }

    this.config = this.options.config ?? resolveAnalyticsConfig();
    this.consent = await readConsent();
    this.enabled = await readEnabled();
    this.provider = this.options.provider ?? this.selectProvider();
    this.initialized = true;

    // §29: contexto global en todos los eventos, solo con datos seguros.
    this.provider.register({
      platform: this.config.platform,
      appVersion: this.config.appVersion,
      buildNumber: this.config.buildNumber,
      locale: this.config.locale,
      environment: this.config.environment,
      schemaVersion: ANALYTICS_SCHEMA_VERSION,
    });

    this.flushPending();
  }

  /** §23/§24. Recalcula el proveedor: pasar a denied deja de enviar de inmediato. */
  async setConsent(state: ConsentState): Promise<void> {
    this.consent = state;
    await writeConsent(state);
    this.refreshProvider();
  }

  async setEnabled(enabled: boolean): Promise<void> {
    this.enabled = enabled;
    await writeEnabled(enabled);
    this.refreshProvider();
  }

  /**
   * §3: el id tiene que ser opaco. Aquí llega el id interno del usuario
   * (un GUID del backend), nunca su correo ni su nombre.
   */
  identify(analyticsId: string, properties?: AnalyticsProperties): void {
    if (!analyticsId) {
      return;
    }

    this.analyticsId = analyticsId;

    if (!this.canSend()) {
      return;
    }

    const { properties: safe } = sanitizeUserProperties(properties);
    this.provider.identify(analyticsId, safe);
  }

  /** §4: vincula el id anónimo del dispositivo con el interno al registrarse. */
  aliasToCurrentUser(analyticsId: string): void {
    if (!analyticsId || !this.canSend()) {
      return;
    }

    this.provider.alias?.(analyticsId);
  }

  setUserProperties(properties: AnalyticsProperties): void {
    if (!this.canSend()) {
      return;
    }

    const { properties: safe } = sanitizeUserProperties(properties);
    this.provider.setUserProperties(safe);
  }

  track(event: AnalyticsEventName, properties?: AnalyticsProperties): void {
    const safe = this.prepare(event, properties);

    if (!safe) {
      return;
    }

    if (!this.initialized) {
      this.queue({ kind: 'track', name: event, properties: safe });
      return;
    }

    this.provider.track(event, safe);
  }

  /**
   * §30: emite el evento como mucho una vez por usuario, sobreviviendo a
   * reinicios de la app.
   *
   * Requiere que `identify()` ya haya ocurrido: sin id de usuario no hay
   * forma de saber de quién es el hito, y marcarlo por dispositivo daría
   * falsos negativos en cuanto alguien entre con otra cuenta.
   */
  async trackOnce(event: AnalyticsEventName, properties?: AnalyticsProperties): Promise<boolean> {
    if (!this.analyticsId) {
      return false;
    }

    const claimed = await claimOnce(this.analyticsId, event);

    if (!claimed) {
      return false;
    }

    this.track(event, properties);
    return true;
  }

  screen(name: AnalyticsScreenName, properties?: AnalyticsProperties): void {
    if (!this.canSend()) {
      return;
    }

    // Las pantallas no tienen esquema propio: solo se les permite el contexto
    // mínimo, para que nadie cuele datos por la puerta de `screen()`.
    const safe: SafeProperties = {};
    const source = properties?.source;
    if (typeof source === 'string') {
      safe.source = source;
    }

    if (!this.initialized) {
      this.queue({ kind: 'screen', name, properties: safe });
      return;
    }

    this.provider.screen(name, safe);
  }

  /**
   * §14: decide si esta vuelta al primer plano abre una sesión nueva.
   * Devuelve true si la abrió, para que el llamador no tenga que duplicar la
   * regla de los 30 minutos.
   */
  async noteAppActive(now: number = Date.now()): Promise<boolean> {
    const lastActive = await readLastActiveAt();
    await writeLastActiveAt(now);

    const isNewSession = lastActive === null || now - lastActive > SESSION_TIMEOUT_MS;

    return isNewSession;
  }

  async noteAppInactive(now: number = Date.now()): Promise<void> {
    await writeLastActiveAt(now);
  }

  /** §3: al cerrar sesión se corta el hilo para no mezclar usuarios en un mismo teléfono. */
  reset(): void {
    this.analyticsId = null;
    this.provider.reset();
  }

  async flush(): Promise<void> {
    await this.provider.flush?.();
  }

  // --- interno -------------------------------------------------------------

  private prepare(event: AnalyticsEventName, properties?: AnalyticsProperties): SafeProperties | null {
    if (!this.canSend()) {
      return null;
    }

    const { properties: safe, dropped } = sanitizeAnalyticsProperties(event, properties);

    if (dropped.length > 0) {
      this.droppedCount += dropped.length;

      if (__DEV__) {
        for (const entry of dropped) {
          // eslint-disable-next-line no-console
          console.warn(
            `[Analytics] "${event}": la propiedad "${entry.key}" no se envió (${entry.reason}). ` +
              'Revisa services/analytics/schema.ts o el sitio que la manda.',
          );
        }
      }
    }

    return safe;
  }

  private canSend(): boolean {
    if (!this.enabled) {
      return false;
    }

    if (this.consent === 'denied') {
      return false;
    }

    if (this.requireExplicitConsent && this.consent !== 'granted') {
      return false;
    }

    return true;
  }

  private queue(event: PendingEvent): void {
    if (this.pending.length >= MAX_PENDING_EVENTS) {
      return;
    }

    this.pending.push(event);
  }

  private flushPending(): void {
    const queued = this.pending;
    this.pending = [];

    for (const event of queued) {
      if (event.kind === 'track') {
        this.provider.track(event.name, event.properties);
      } else {
        this.provider.screen(event.name, event.properties);
      }
    }
  }

  private refreshProvider(): void {
    if (!this.initialized || this.options.provider) {
      return;
    }

    this.provider = this.selectProvider();
  }

  private selectProvider(): AnalyticsProvider {
    if (!this.canSend()) {
      return new NoopAnalyticsProvider();
    }

    const config = this.config;

    if (config?.apiKey && config.environment !== 'development') {
      const provider = PostHogAnalyticsProvider.create({ apiKey: config.apiKey, host: config.host });

      if (provider) {
        return provider;
      }
      // Sin SDK instalado se cae a debug/noop en vez de romper el arranque.
    }

    return __DEV__ ? new DebugAnalyticsProvider() : new NoopAnalyticsProvider();
  }
}

/**
 * §5: propiedades de usuario. Se filtran con las mismas reglas de forma que
 * las de evento, pero con su propia allowlist -- no pertenecen a ningún
 * evento del catálogo.
 */
const ALLOWED_USER_PROPERTIES: ReadonlySet<string> = new Set([
  'plan',
  'onboardingCompleted',
  'hasImportedStatement',
  'hasCashAccount',
  'hasNotificationsEnabled',
  'hasPulseEnabled',
  'numberOfConnectedSourcesBucket',
  'appVersion',
  'platform',
  'locale',
]);

function sanitizeUserProperties(properties: AnalyticsProperties | undefined): { properties: SafeProperties } {
  const safe: SafeProperties = {};

  if (!properties) {
    return { properties: safe };
  }

  for (const [key, value] of Object.entries(properties)) {
    if (!ALLOWED_USER_PROPERTIES.has(key) || value === null || value === undefined) {
      continue;
    }

    if (typeof value === 'boolean') {
      safe[key] = value;
      continue;
    }

    if (typeof value === 'string' && value.length <= 40 && /^[a-z0-9_.:-]+$/i.test(value)) {
      safe[key] = value.toLowerCase();
    }
  }

  return { properties: safe };
}

/** Instancia única que usa la app. Los tests construyen la suya. */
export const analytics = new AnalyticsService({ requireExplicitConsent: true });
