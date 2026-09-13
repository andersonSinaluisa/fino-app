import { sanitizeAnalyticsProperties } from '../../services/analytics/sanitize';
import { AnalyticsEvent } from '../../services/analytics/events';

describe('sanitizeAnalyticsProperties', () => {
  it('deja pasar las propiedades declaradas en el esquema del evento', () => {
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.QuickEntrySaved, {
      source: 'home',
      entryMode: 'keypad',
      transactionType: 'expense',
      durationBucket: 'under_2s',
      offline: false,
    });

    expect(properties).toEqual({
      source: 'home',
      entryMode: 'keypad',
      transactionType: 'expense',
      durationBucket: 'under_2s',
      offline: false,
    });
    expect(dropped).toHaveLength(0);
  });

  it('descarta cualquier propiedad fuera de la allowlist', () => {
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.QuickEntrySaved, {
      entryMode: 'keypad',
      amount: 12.5,
      merchant: 'supermaxi',
      accountName: 'ahorros',
    });

    expect(properties).toEqual({ entryMode: 'keypad' });
    expect(dropped.map((entry) => entry.key).sort()).toEqual(['accountName', 'amount', 'merchant']);
  });

  it('nunca deja salir un número por una clave que no sea de conteo', () => {
    // `durationBucket` SÍ está en la allowlist del evento, pero un número por
    // ahí sería una duración cruda: se descarta igual.
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.QuickEntrySaved, {
      durationBucket: 1874,
    });

    expect(properties).toEqual({});
    expect(dropped[0]).toEqual({ key: 'durationBucket', reason: 'number_not_allowed_here' });
  });

  it('acepta números solo en las claves de conteo y dentro de rango', () => {
    expect(
      sanitizeAnalyticsProperties(AnalyticsEvent.BankTutorialStepViewed, { step: 3 }).properties,
    ).toEqual({ step: 3 });

    expect(
      sanitizeAnalyticsProperties(AnalyticsEvent.BankTutorialStepViewed, { step: 9999 }).properties,
    ).toEqual({});
  });

  it('rechaza un valor de texto que parezca contenido del usuario', () => {
    // La clave está permitida; el valor no tiene forma de símbolo cerrado.
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.BankSelected, {
      bankCode: 'Supermaxi Urdesa - almuerzo del martes',
    });

    expect(properties).toEqual({});
    expect(dropped[0]?.reason).toBe('string_looks_like_content');
  });

  it('normaliza los símbolos válidos a minúsculas', () => {
    expect(sanitizeAnalyticsProperties(AnalyticsEvent.BankSelected, { bankCode: 'PICHINCHA' }).properties).toEqual({
      bankCode: 'pichincha',
    });
  });

  it('ignora null y undefined sin contarlos como descartes', () => {
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.QuickEntrySaved, {
      entryMode: 'voice',
      durationBucket: undefined,
      source: null,
    });

    expect(properties).toEqual({ entryMode: 'voice' });
    expect(dropped).toHaveLength(0);
  });

  it('descarta objetos y arrays', () => {
    const { properties, dropped } = sanitizeAnalyticsProperties(AnalyticsEvent.QuickEntrySaved, {
      entryMode: { nested: true } as never,
    });

    expect(properties).toEqual({});
    expect(dropped[0]?.reason).toBe('unsupported_type');
  });

  it('un evento sin propiedades declaradas no deja pasar ninguna', () => {
    const { properties } = sanitizeAnalyticsProperties(AnalyticsEvent.OnboardingStarted, {
      anything: 'home',
    });

    expect(properties).toEqual({});
  });
});
