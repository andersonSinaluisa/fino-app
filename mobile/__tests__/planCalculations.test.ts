import { monthRangeLabel } from '../utils/planCalculations';

/**
 * Disponible/Comprometido ya no se calculan en el teléfono (ver
 * CommittedMoneyCalculatorTests en el backend). Aquí queda solo el formato.
 */
describe('plan calculations', () => {
  it('formats the current month range in Spanish', () => {
    expect(monthRangeLabel(new Date(2026, 8, 6))).toBe('1 sept - 30 sept');
  });
});
