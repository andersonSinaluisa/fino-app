import type { AnalyticsProvider } from './types';
import type { SafeProperties } from '../sanitize';

/**
 * Superficie mínima del SDK que de verdad usamos.
 *
 * Se declara aquí a propósito, en vez de importar los tipos de
 * `posthog-react-native`: así el proyecto compila y los tests corren aunque
 * el paquete todavía no esté instalado, y el día que se cambie de proveedor
 * no hay tipos del SDK filtrados por el resto del código.
 */
interface PostHogLike {
  capture(event: string, properties?: Record<string, unknown>): void;
  identify(distinctId: string, properties?: Record<string, unknown>): void;
  screen(name: string, properties?: Record<string, unknown>): void;
  register(properties: Record<string, unknown>): void;
  alias(alias: string): void;
  reset(): void;
  flush?(): Promise<void>;
}

export interface PostHogOptions {
  apiKey: string;
  host: string;
}

/**
 * Adaptador de PostHog.
 *
 * Dos decisiones que importan en una app financiera:
 *
 * 1. Se instancia el cliente DIRECTAMENTE. No se monta `<PostHogProvider>`,
 *    que es el componente que activa el autocapture de PostHog. El
 *    autocapture registra toques y el texto de los elementos tocados: en FINO
 *    eso capturaría montos, nombres de cuenta y descripciones de movimientos
 *    -- exactamente lo que no puede salir. Sin ese componente, el SDK solo
 *    manda lo que le pasamos explícitamente.
 *
 * 2. `disableGeoip: true` y sin session replay. La IP resuelta a ubicación no
 *    hace falta para ninguna de las preguntas de producto, y una grabación de
 *    sesión de una app de finanzas es un volcado de datos financieros.
 */
export class PostHogAnalyticsProvider implements AnalyticsProvider {
  readonly name = 'posthog';

  private constructor(private readonly client: PostHogLike) {}

  /**
   * Devuelve null si el SDK no está instalado o no arranca, para que la app
   * caiga al proveedor de respaldo en vez de romperse. Analytics nunca puede
   * ser una dependencia de que el producto funcione.
   */
  static create(options: PostHogOptions): PostHogAnalyticsProvider | null {
    const client = loadClient(options);
    return client ? new PostHogAnalyticsProvider(client) : null;
  }

  identify(id: string, properties?: SafeProperties): void {
    this.client.identify(id, properties);
  }

  setUserProperties(properties: SafeProperties): void {
    // PostHog fija propiedades de persona a través de $set en un evento.
    this.client.capture('$set', { $set: properties });
  }

  register(properties: SafeProperties): void {
    this.client.register(properties);
  }

  track(event: string, properties: SafeProperties): void {
    this.client.capture(event, properties);
  }

  screen(name: string, properties: SafeProperties): void {
    this.client.screen(name, properties);
  }

  alias(id: string): void {
    this.client.alias(id);
  }

  reset(): void {
    this.client.reset();
  }

  async flush(): Promise<void> {
    await this.client.flush?.();
  }
}

function loadClient(options: PostHogOptions): PostHogLike | null {
  try {
    // `require` protegido a propósito: el paquete es opcional hasta que se
    // instale y se haga un build nativo nuevo.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const module = require('posthog-react-native') as { PostHog?: new (key: string, config: unknown) => PostHogLike };
    const PostHog = module?.PostHog;

    if (typeof PostHog !== 'function') {
      return null;
    }

    return new PostHog(options.apiKey, {
      host: options.host,
      // Ver el comentario de la clase: nada de replay ni de geolocalización.
      enableSessionReplay: false,
      disableGeoip: true,
      // Las sesiones y el ciclo de vida los maneja `session.ts`, con la
      // ventana de inactividad que decidimos nosotros (§14). Dejar que el SDK
      // mande también los suyos duplicaría `app_opened`.
      captureAppLifecycleEvents: false,
    });
  } catch {
    return null;
  }
}
