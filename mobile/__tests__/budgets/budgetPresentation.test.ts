import {
  budgetStatusMessage,
  dayAfter,
  dayBefore,
  parseBudgetAmount,
  parseDayInput,
  progressFill,
} from '../../utils/budgets';
import type { BudgetProgress } from '../../types/api';

function progress(overrides: Partial<BudgetProgress> = {}): BudgetProgress {
  return {
    amount: 300,
    spent: 120,
    remaining: 180,
    overspent: 0,
    percentUsed: 40,
    level: 'Normal',
    reserved: 0,
    daysInWindow: 30,
    daysElapsed: 19,
    daysRemaining: 12,
    dailyAllowance: 15,
    projectedSpend: 189.47,
    isCurrentWindow: true,
    ...overrides,
  };
}

/**
 * Solo presentación: el nivel y los montos llegan del backend; aquí se
 * comprueba que el mensaje dice lo mismo que el nivel sin depender del color,
 * y que "ocultar montos" también los oculta en el texto.
 */
describe('budgetStatusMessage', () => {
  it('normal: te quedan', () => {
    expect(budgetStatusMessage(progress())).toBe('Te quedan $180.00');
  });

  it('atención: porcentaje usado', () => {
    expect(budgetStatusMessage(progress({ level: 'Attention', percentUsed: 82.4, remaining: 54 }))).toBe('Has usado el 82%');
  });

  it('cerca del límite: lo poco que queda', () => {
    expect(budgetStatusMessage(progress({ level: 'NearLimit', percentUsed: 96, remaining: 12 }))).toBe('Te quedan $12.00');
  });

  it('excedido: por cuánto', () => {
    expect(budgetStatusMessage(progress({ level: 'Exceeded', percentUsed: 108, remaining: 0, overspent: 24 }))).toBe(
      'Excediste tu presupuesto por $24.00',
    );
  });

  it('con montos ocultos no aparece ningún monto', () => {
    expect(budgetStatusMessage(progress(), true)).not.toContain('$');
    expect(budgetStatusMessage(progress({ level: 'Exceeded', overspent: 24, percentUsed: 108 }), true)).not.toContain('$');
  });
});

describe('progressFill', () => {
  it('nunca pasa de 100 ni baja de 0', () => {
    expect(progressFill(progress({ percentUsed: 250 }))).toBe(100);
    expect(progressFill(progress({ percentUsed: 0 }))).toBe(0);
    expect(progressFill(progress({ percentUsed: 40 }))).toBe(40);
  });
});

describe('fechas y montos del formulario', () => {
  it('lee montos con coma o punto y rechaza lo que no es monto', () => {
    expect(parseBudgetAmount('300')).toBe(300);
    expect(parseBudgetAmount('12,50')).toBe(12.5);
    expect(parseBudgetAmount('$1,200.00')).toBe(1200);
    expect(parseBudgetAmount('0')).toBeNull();
    expect(parseBudgetAmount('-5')).toBeNull();
    expect(parseBudgetAmount('abc')).toBeNull();
    expect(parseBudgetAmount('1.234')).toBeNull();
  });

  it('acepta fechas futuras pero no fechas imposibles', () => {
    expect(parseDayInput('20/12/2030')).toBe('2030-12-20');
    expect(parseDayInput('31/02/2026')).toBeNull();
  });

  it('navega entre meses por el borde de la ventana', () => {
    expect(dayBefore('2026-03-01')).toBe('2026-02-28');
    expect(dayAfter('2026-02-28')).toBe('2026-03-01');
    expect(dayAfter('2026-12-31')).toBe('2027-01-01');
  });
});
