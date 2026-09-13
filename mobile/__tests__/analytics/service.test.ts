import { AnalyticsService } from '../../services/analytics/service';
import { AnalyticsEvent, AnalyticsScreen } from '../../services/analytics/events';
import { DebugAnalyticsProvider } from '../../services/analytics/providers/debug';
import type { AnalyticsProvider } from '../../services/analytics/providers/types';
import type { SafeProperties } from '../../services/analytics/sanitize';
import type { AnalyticsConfig } from '../../services/analytics/config';

interface Recorded {
  name: string;
  properties: SafeProperties;
}

class FakeProvider implements AnalyticsProvider {
  readonly name = 'fake';
  events: Recorded[] = [];
  screens: Recorded[] = [];
  identified: { id: string; properties?: SafeProperties }[] = [];
  superProperties: SafeProperties = {};
  aliases: string[] = [];
  resets = 0;

  identify(id: string, properties?: SafeProperties): void {
    this.identified.push({ id, properties });
  }

  setUserProperties(properties: SafeProperties): void {
    this.superProperties = { ...this.superProperties, ...properties };
  }

  register(properties: SafeProperties): void {
    this.superProperties = { ...this.superProperties, ...properties };
  }

  track(name: string, properties: SafeProperties): void {
    this.events.push({ name, properties });
  }

  screen(name: string, properties: SafeProperties): void {
    this.screens.push({ name, properties });
  }

  alias(id: string): void {
    this.aliases.push(id);
  }

  reset(): void {
    this.resets += 1;
  }
}

const config: AnalyticsConfig = {
  apiKey: null,
  host: 'https://example.invalid',
  environment: 'test',
  appVersion: '1.2.3',
  buildNumber: '42',
  platform: 'ios',
  locale: 'es-ec',
};

async function build(overrides: Partial<ConstructorParameters<typeof AnalyticsService>[0]> = {}) {
  const provider = new FakeProvider();
  const service = new AnalyticsService({ provider, config, ...overrides });
  await service.init();
  return { provider, service };
}

describe('AnalyticsService', () => {
  it('envía un evento del catálogo con sus propiedades permitidas', async () => {
    const { provider, service } = await build();

    service.track(AnalyticsEvent.QuickEntryOpened, { source: 'home' });

    expect(provider.events).toEqual([{ name: 'quick_entry_opened', properties: { source: 'home' } }]);
  });

  it('no deja salir una propiedad prohibida aunque el llamador la mande', async () => {
    const { provider, service } = await build();

    service.track(AnalyticsEvent.QuickEntrySaved, {
      entryMode: 'keypad',
      amount: 42.75,
      description: 'almuerzo',
    });

    expect(provider.events[0]?.properties).toEqual({ entryMode: 'keypad' });
    expect(service.droppedPropertyCount).toBe(2);
  });

  it('registra el contexto global seguro al arrancar', async () => {
    const { provider } = await build();

    expect(provider.superProperties).toEqual({
      platform: 'ios',
      appVersion: '1.2.3',
      buildNumber: '42',
      locale: 'es-ec',
      environment: 'test',
      schemaVersion: 1,
    });
  });

  it('identifica con el id interno opaco y nunca con datos personales', async () => {
    const { provider, service } = await build();

    service.identify('8f3a0c12-0000-4000-8000-000000000001', {
      plan: 'free',
      email: 'anderson@example.com',
      displayName: 'Anderson',
    });

    expect(provider.identified).toHaveLength(1);
    expect(provider.identified[0]?.id).toBe('8f3a0c12-0000-4000-8000-000000000001');
    expect(provider.identified[0]?.properties).toEqual({ plan: 'free' });
  });

  it('reset corta el hilo del usuario anterior', async () => {
    const { provider, service } = await build();

    service.identify('user-a');
    service.reset();

    expect(provider.resets).toBe(1);
  });

  it('trackOnce emite una sola vez por usuario y sobrevive a otra instancia', async () => {
    const { provider, service } = await build();
    service.identify('user-once-1');

    await expect(service.trackOnce(AnalyticsEvent.FirstValueReached, { valueSource: 'cash_manual' })).resolves.toBe(
      true,
    );
    await expect(service.trackOnce(AnalyticsEvent.FirstValueReached, { valueSource: 'cash_manual' })).resolves.toBe(
      false,
    );

    expect(provider.events.filter((event) => event.name === 'first_value_reached')).toHaveLength(1);

    // Una instancia nueva (equivale a reiniciar la app) tampoco lo repite.
    const second = new AnalyticsService({ provider, config });
    await second.init();
    second.identify('user-once-1');
    await expect(second.trackOnce(AnalyticsEvent.FirstValueReached)).resolves.toBe(false);
  });

  it('trackOnce distingue entre usuarios del mismo dispositivo', async () => {
    const { service } = await build();

    service.identify('user-x');
    await expect(service.trackOnce(AnalyticsEvent.FirstCashEntry)).resolves.toBe(true);

    service.reset();
    service.identify('user-y');
    await expect(service.trackOnce(AnalyticsEvent.FirstCashEntry)).resolves.toBe(true);
  });

  it('trackOnce no hace nada sin usuario identificado', async () => {
    const { service } = await build();

    await expect(service.trackOnce(AnalyticsEvent.FirstPulseOpened)).resolves.toBe(false);
  });

  it('con analytics desactivado no envía nada', async () => {
    const { provider, service } = await build();

    await service.setEnabled(false);
    service.track(AnalyticsEvent.HomeViewed, { hasData: true });
    service.screen(AnalyticsScreen.Home);
    service.identify('user-disabled');

    expect(provider.events).toHaveLength(0);
    expect(provider.screens).toHaveLength(0);
    expect(provider.identified).toHaveLength(0);
  });

  it('vuelve a enviar cuando se reactiva', async () => {
    const { provider, service } = await build();

    await service.setEnabled(false);
    await service.setEnabled(true);
    service.track(AnalyticsEvent.HomeViewed, { hasData: true });

    expect(provider.events).toHaveLength(1);
  });

  it('con consentimiento denegado no envía', async () => {
    const { provider, service } = await build();

    await service.setConsent('denied');
    service.track(AnalyticsEvent.HomeViewed, { hasData: true });

    expect(provider.events).toHaveLength(0);
  });

  it('con consentimiento exigido, "unknown" no envía', async () => {
    const provider = new FakeProvider();
    const service = new AnalyticsService({ provider, config, requireExplicitConsent: true });
    await service.init();

    service.track(AnalyticsEvent.HomeViewed, { hasData: true });
    expect(provider.events).toHaveLength(0);

    await service.setConsent('granted');
    service.track(AnalyticsEvent.HomeViewed, { hasData: true });
    expect(provider.events).toHaveLength(1);
  });

  it('screen solo deja pasar el nombre y el origen', async () => {
    const { provider, service } = await build();

    service.screen(AnalyticsScreen.Statistics, { source: 'home', accountName: 'ahorros' });

    expect(provider.screens).toEqual([{ name: 'statistics', properties: { source: 'home' } }]);
  });

  it('guarda los eventos emitidos antes de init y los suelta al arrancar', async () => {
    const provider = new FakeProvider();
    const service = new AnalyticsService({ provider, config });

    service.track(AnalyticsEvent.OnboardingStarted);
    expect(provider.events).toHaveLength(0);

    await service.init();
    expect(provider.events).toEqual([{ name: 'onboarding_started', properties: {} }]);
  });

  it('abre sesión nueva solo si la pausa superó la ventana de inactividad', async () => {
    const { service } = await build();
    const start = Date.parse('2026-01-01T10:00:00.000Z');

    await expect(service.noteAppActive(start)).resolves.toBe(true);
    // Cinco minutos después: es la misma sesión.
    await expect(service.noteAppActive(start + 5 * 60_000)).resolves.toBe(false);
    // Cuarenta minutos después de la última actividad: sesión nueva.
    await expect(service.noteAppActive(start + 45 * 60_000)).resolves.toBe(true);
  });

  it('el proveedor de debug imprime sin enviar', async () => {
    const lines: string[] = [];
    const provider = new DebugAnalyticsProvider((message) => lines.push(message));
    const service = new AnalyticsService({ provider, config });
    await service.init();

    service.track(AnalyticsEvent.PulseOpened, { pulseKind: 'spending_spike' });

    expect(provider.name).toBe('debug');
    expect(lines).toContain('[Analytics] event: pulse_opened');
  });
});
