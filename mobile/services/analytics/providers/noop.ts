import type { AnalyticsProvider } from './types';

/**
 * No manda nada a ninguna parte.
 *
 * Es el proveedor que se usa cuando el consentimiento todavía no está
 * decidido, cuando el usuario desactivó analytics, y en los tests. Que la
 * opción "no enviar nada" sea un proveedor más (y no un `if` repartido por el
 * código) es lo que hace que desactivar analytics sea de verdad desactivarlo.
 */
export class NoopAnalyticsProvider implements AnalyticsProvider {
  readonly name = 'noop';

  identify(): void {}
  setUserProperties(): void {}
  register(): void {}
  track(): void {}
  screen(): void {}
  alias(): void {}
  reset(): void {}
  async flush(): Promise<void> {}
}
