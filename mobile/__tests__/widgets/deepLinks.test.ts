import { widgetDeepLinks } from '../../lib/widgets/deepLinks';

describe('widgetDeepLinks', () => {
  it('uses the app scheme with the triple-slash Expo convention', () => {
    expect(widgetDeepLinks.home()).toBe('fino:///');
    expect(widgetDeepLinks.accounts()).toBe('fino:///cuentas');
    expect(widgetDeepLinks.statistics()).toBe('fino:///estadisticas');
  });

  it('encodes the category/account id into the movements route', () => {
    expect(widgetDeepLinks.movementsByCategory('cat-1')).toBe('fino:///movimientos?categoryId=cat-1');
    expect(widgetDeepLinks.movementsByAccount('acc 1')).toBe('fino:///movimientos?accountId=acc%201');
  });
});
