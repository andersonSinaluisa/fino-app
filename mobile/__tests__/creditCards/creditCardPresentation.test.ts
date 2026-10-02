import {
  dueLabel,
  formatCycleRange,
  formatDueDate,
  formatPercent,
  installmentProgressLabel,
  isToday,
  nextInstallmentLabel,
  parseLocalDate,
  toLocalDateString,
  utilizationFill,
} from '../../utils/creditCards';
import type { InstallmentPlan } from '../../types/api';

function plan(overrides: Partial<InstallmentPlan> = {}): InstallmentPlan {
  return {
    id: 'p1',
    transactionId: 't1',
    description: 'Laptop',
    categoryName: 'Compras',
    purchaseDate: '2026-05-10',
    originalAmount: 1200,
    numberOfInstallments: 12,
    installmentAmount: 100,
    interestRate: null,
    billedInstallments: 4,
    nextInstallmentNumber: 5,
    nextInstallmentAmount: 100,
    nextInstallmentClosingDate: '2026-09-15',
    nextInstallmentDueDate: '2026-09-30',
    outstandingAmount: 800,
    status: 'Active',
    installments: [],
    ...overrides,
  };
}

/**
 * Tarjetas de crédito: la app solo PRESENTA lo que calcula el backend. Estas
 * pruebas cuidan que la presentación no corra fechas ni invente cifras.
 */
describe('presentación de tarjetas', () => {
  it('lee las fechas del backend como fechas de calendario, sin correrlas por la zona horaria', () => {
    expect(parseLocalDate('2026-10-30')).toEqual({ year: 2026, month: 10, day: 30 });
    expect(formatDueDate('2026-10-30')).toBe('30 OCT');
    expect(formatDueDate('2028-02-29')).toBe('29 FEB');
    expect(formatCycleRange('2026-09-16', '2026-10-15')).toBe('16 sep – 15 oct');
  });

  it('escribe la fecha local que eligió la persona', () => {
    expect(toLocalDateString(new Date(2026, 2, 5))).toBe('2026-03-05');
    expect(isToday(new Date(2026, 2, 5, 23, 59), new Date(2026, 2, 5, 8, 0))).toBe(true);
    expect(isToday(new Date(2026, 2, 4), new Date(2026, 2, 5))).toBe(false);
  });

  it('dice cuándo vence con el conteo del backend', () => {
    expect(dueLabel({ daysUntilDue: 3, isOverdue: false })).toBe('Vence en 3 días');
    expect(dueLabel({ daysUntilDue: 1, isOverdue: false })).toBe('Vence mañana');
    expect(dueLabel({ daysUntilDue: 0, isOverdue: false })).toBe('Vence hoy');
    expect(dueLabel({ daysUntilDue: -2, isOverdue: true })).toBe('Venció hace 2 días');
  });

  it('la barra de utilización nunca pasa de lleno ni baja de vacío', () => {
    expect(utilizationFill(37.5)).toBe(37.5);
    expect(utilizationFill(140)).toBe(100);
    expect(utilizationFill(null)).toBe(0);
    expect(formatPercent(37.5)).toBe('37.5%');
    expect(formatPercent(40)).toBe('40%');
  });

  it('una compra a cuotas se lee como "4/12" con su próxima cuota', () => {
    expect(installmentProgressLabel(plan())).toBe('4/12');
    expect(nextInstallmentLabel(plan())).toBe('Próxima $100.00 · corte 15 SEP');
    expect(nextInstallmentLabel(plan(), true)).toBe('Próxima •••••• · corte 15 SEP');
    expect(nextInstallmentLabel(plan({ status: 'Cancelled' }))).toBe('Precancelado');
    expect(nextInstallmentLabel(plan({ nextInstallmentAmount: null, nextInstallmentClosingDate: null }))).toBe('Todas las cuotas facturadas');
  });
});
