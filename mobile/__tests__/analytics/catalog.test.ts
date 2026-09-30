import { AnalyticsEvent, AnalyticsScreen } from '../../services/analytics/events';
import { EVENT_SCHEMA, SUSPICIOUS_KEY_PATTERN, SUSPICIOUS_PATTERN_EXCEPTIONS } from '../../services/analytics/schema';
import { resolveScreen } from '../../services/analytics/screens';

describe('catálogo de eventos', () => {
  const eventNames = Object.values(AnalyticsEvent);

  it('todos los eventos del catálogo tienen esquema declarado', () => {
    for (const event of eventNames) {
      expect(EVENT_SCHEMA[event]).toBeDefined();
    }
  });

  it('no hay esquemas huérfanos', () => {
    expect(Object.keys(EVENT_SCHEMA).sort()).toEqual([...eventNames].sort());
  });

  it('los nombres de evento son snake_case', () => {
    for (const event of eventNames) {
      expect(event).toMatch(/^[a-z][a-z0-9_]*$/);
    }
  });

  it('no hay nombres de evento repetidos', () => {
    expect(new Set(eventNames).size).toBe(eventNames.length);
  });

  /**
   * La prueba que de verdad protege la promesa de privacidad: si alguien
   * añade `amount`, `merchant` o `description` a la allowlist de cualquier
   * evento, esto falla antes de llegar a producción.
   */
  it('ninguna allowlist declara una propiedad que parezca dato sensible', () => {
    const offenders: string[] = [];

    for (const [event, keys] of Object.entries(EVENT_SCHEMA)) {
      for (const key of keys) {
        if (!SUSPICIOUS_PATTERN_EXCEPTIONS.has(key) && SUSPICIOUS_KEY_PATTERN.test(key)) {
          offenders.push(`${event}.${key}`);
        }
      }
    }

    expect(offenders).toEqual([]);
  });
});

describe('resolución de pantallas', () => {
  it('mapea las rutas principales', () => {
    expect(resolveScreen('/')).toBe(AnalyticsScreen.Home);
    expect(resolveScreen('/movimientos')).toBe(AnalyticsScreen.Movements);
    expect(resolveScreen('/estadisticas')).toBe(AnalyticsScreen.Statistics);
    expect(resolveScreen('/perfil')).toBe(AnalyticsScreen.Profile);
  });

  it('el detalle de un pulso no lleva el id en el nombre', () => {
    expect(resolveScreen('/pulso/9a7f1c30-0000-4000-8000-000000000002')).toBe(AnalyticsScreen.PulseDetail);
  });

  it('los modales pequeños no cuentan como pantalla', () => {
    expect(resolveScreen('/efectivo/ajustar')).toBeNull();
    expect(resolveScreen('/cuentas/agregar')).toBeNull();
    expect(resolveScreen('/compartir')).toBeNull();
    expect(resolveScreen('/registrar')).toBeNull();
  });
});

describe('pantallas de presupuestos', () => {
  it('la lista, el detalle (sin id) y el desglose de Comprometido', () => {
    expect(resolveScreen('/presupuestos')).toBe(AnalyticsScreen.Budgets);
    expect(resolveScreen('/presupuestos/01a0f001-d9a0-78ef-873c-6e478754633a')).toBe(AnalyticsScreen.BudgetDetail);
    expect(resolveScreen('/comprometido')).toBe(AnalyticsScreen.Committed);
  });
});
