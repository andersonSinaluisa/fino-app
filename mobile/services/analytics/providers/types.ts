import type { SafeProperties } from '../sanitize';

/**
 * El contrato que ve `AnalyticsService`. Ningún componente de la app conoce
 * este archivo ni mucho menos el SDK de debajo: cambiar de proveedor es
 * escribir otra implementación de esto, no tocar una sola pantalla.
 */
export interface AnalyticsProvider {
  readonly name: string;

  /** Asocia los eventos siguientes a un usuario. `id` es siempre opaco. */
  identify(id: string, properties?: SafeProperties): void;

  /** Propiedades de usuario, sin evento asociado. */
  setUserProperties(properties: SafeProperties): void;

  /** Propiedades que acompañan a TODOS los eventos (contexto global, §29). */
  register(properties: SafeProperties): void;

  track(event: string, properties: SafeProperties): void;

  screen(name: string, properties: SafeProperties): void;

  /**
   * §4: vincula el id anónimo previo con el id interno al registrarse, para
   * no perder el tramo install -> onboarding -> signup.
   */
  alias?(id: string): void;

  /** §3: corta el hilo al cerrar sesión, para que dos personas que usan el mismo teléfono no se mezclen. */
  reset(): void;

  flush?(): Promise<void>;
}
