import type { AnalyticsProvider } from './types';
import type { SafeProperties } from '../sanitize';

/**
 * §22: imprime en consola en vez de enviar.
 *
 * Sirve para validar el catálogo entero sin ensuciar producción y sin
 * necesitar una cuenta del proveedor. Lo que imprime es exactamente lo que
 * habría salido -- ya pasado por el sanitizador -- así que si aquí no
 * aparece una propiedad, tampoco iba a salir.
 */
export class DebugAnalyticsProvider implements AnalyticsProvider {
  readonly name = 'debug';

  constructor(private readonly log: (message: string, payload?: unknown) => void = defaultLog) {}

  identify(id: string, properties?: SafeProperties): void {
    this.log(`[Analytics] identify: ${id}`, properties ?? {});
  }

  setUserProperties(properties: SafeProperties): void {
    this.log('[Analytics] user properties', properties);
  }

  register(properties: SafeProperties): void {
    this.log('[Analytics] super properties', properties);
  }

  track(event: string, properties: SafeProperties): void {
    this.log(`[Analytics] event: ${event}`, properties);
  }

  screen(name: string, properties: SafeProperties): void {
    this.log(`[Analytics] screen: ${name}`, properties);
  }

  alias(id: string): void {
    this.log(`[Analytics] alias: ${id}`);
  }

  reset(): void {
    this.log('[Analytics] reset');
  }

  async flush(): Promise<void> {}
}

function defaultLog(message: string, payload?: unknown): void {
  // eslint-disable-next-line no-console
  console.log(message, payload ?? '');
}
