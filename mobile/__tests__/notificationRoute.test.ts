import { notificationKind, notificationRoute } from '../utils/notificationRoute';

describe('notificationRoute', () => {
  it('opens the screen each reminder is about', () => {
    expect(notificationRoute({ cardId: 'c1' })).toBe('/tarjetas/c1');
    expect(notificationRoute({ budgetId: 'b1' })).toBe('/presupuestos/b1');
    expect(notificationRoute({ accountId: 'a1' })).toEqual({ pathname: '/cuentas/importar', params: { accountId: 'a1' } });
    expect(notificationRoute({ screen: 'estadisticas' })).toBe('/(tabs)/estadisticas');
  });

  it('keeps the movement and pulse deep links', () => {
    expect(notificationRoute({ transactionId: 't1', notificationId: 'n' })).toBe('/movimiento/t1');
    expect(notificationRoute({ pulseId: 'p1' })).toBe('/pulso/p1');
  });

  it('falls back to nothing specific', () => {
    expect(notificationRoute(undefined)).toBeNull();
    expect(notificationRoute({ notificationId: 'n', type: 'test' })).toBeNull();
    expect(notificationRoute({ screen: 'otra' })).toBeNull();
  });

  it('reports only the kind to analytics', () => {
    expect(notificationKind({ cardId: 'c1' })).toBe('card');
    expect(notificationKind({ transactionId: 't' })).toBe('movement');
    expect(notificationKind(null)).toBe('other');
  });
});
