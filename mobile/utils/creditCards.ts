import type {
  CardMovementType,
  CardNetwork,
  CreditCardNextPayment,
  CreditCardSummary,
  InstallmentPlan,
  StatementStatus,
} from '../types/api';
import { formatCurrency } from './format';

/**
 * Tarjetas de crédito: SOLO presentación. Deuda, cupo, utilización, total de
 * cada estado, pagado, pendiente, próximo pago y lo que va a Comprometido
 * llegan calculados del backend (CreditCardCalculator). Aquí solo se decide
 * cómo decirlo en español -- nunca un monto ni una regla.
 */

const SHORT_MONTHS = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
const MONTHS = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

/**
 * "2026-10-30" (fecha LOCAL del backend, sin hora) -> partes. Nunca `new Date("2026-10-30")`:
 * eso es medianoche UTC y en Ecuador se mostraría el 29.
 */
export function parseLocalDate(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) {
    return null;
  }

  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

/** "30 OCT" -- como la imprime un estado de cuenta. */
export function formatDueDate(value: string): string {
  const date = parseLocalDate(value);
  if (!date) {
    return value;
  }

  return `${date.day} ${(SHORT_MONTHS[date.month - 1] ?? '').toUpperCase()}`;
}

/** "30 de octubre". */
export function formatLongDate(value: string): string {
  const date = parseLocalDate(value);
  if (!date) {
    return value;
  }

  return `${date.day} de ${MONTHS[date.month - 1] ?? ''}`;
}

/** "16 sep – 15 oct". */
export function formatCycleRange(start: string, end: string): string {
  const from = parseLocalDate(start);
  const to = parseLocalDate(end);
  if (!from || !to) {
    return `${start} – ${end}`;
  }

  return `${from.day} ${SHORT_MONTHS[from.month - 1]} – ${to.day} ${SHORT_MONTHS[to.month - 1]}`;
}

/** "yyyy-MM-dd" de una fecha de calendario local (la que la persona eligió). */
export function toLocalDateString(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * ¿Es hoy (calendario local)? Un pago o movimiento de "hoy" se manda sin hora
 * para que el servidor use "ahora" y quede después de un saldo verificado
 * registrado hoy más temprano.
 */
export function isToday(date: Date, now = new Date()): boolean {
  return date.getFullYear() === now.getFullYear() && date.getMonth() === now.getMonth() && date.getDate() === now.getDate();
}

/** "Visa Pichincha •••• 4582". */
export function cardTitle(card: Pick<CreditCardSummary, 'name' | 'lastFour'>): string {
  return card.lastFour ? `${card.name} •••• ${card.lastFour}` : card.name;
}

export const NETWORK_LABELS: Record<CardNetwork, string> = {
  Visa: 'Visa',
  Mastercard: 'Mastercard',
  AmericanExpress: 'American Express',
  Diners: 'Diners Club',
  Discover: 'Discover',
  Other: 'Otra',
};

export const MOVEMENT_TYPE_LABELS: Record<CardMovementType, string> = {
  Purchase: 'Compra',
  Refund: 'Devolución',
  Payment: 'Pago a la tarjeta',
  Interest: 'Intereses',
  Fee: 'Comisión',
  CashAdvance: 'Avance en efectivo',
  Adjustment: 'Ajuste',
};

/** Una frase que explica qué hace cada tipo con el dinero (para el selector). */
export const MOVEMENT_TYPE_HINTS: Record<CardMovementType, string> = {
  Purchase: 'Aumenta la deuda y cuenta como gasto.',
  Refund: 'Reduce la deuda y resta del gasto de su categoría.',
  Payment: 'Reduce la deuda. No es ingreso ni gasto.',
  Interest: 'Aumenta la deuda. Costo financiero, no consumo.',
  Fee: 'Aumenta la deuda. Costo financiero, no consumo.',
  CashAdvance: 'Aumenta la deuda; el dinero pasa a tu efectivo. No es gasto.',
  Adjustment: 'Corrige la deuda. No es ingreso ni gasto.',
};

/** Tipos que se registran a mano desde la tarjeta (el pago tiene su propio flujo). */
export const MANUAL_MOVEMENT_TYPES: Exclude<CardMovementType, 'Payment'>[] = [
  'Purchase',
  'Refund',
  'Interest',
  'Fee',
  'CashAdvance',
  'Adjustment',
];

export const STATEMENT_STATUS_PRESENTATION: Record<
  StatementStatus,
  { label: string; tone: 'positive' | 'neutral' | 'attention' | 'danger' | 'accent' }
> = {
  Open: { label: 'Abierto', tone: 'neutral' },
  Closed: { label: 'Por pagar', tone: 'attention' },
  PartiallyPaid: { label: 'Pago parcial', tone: 'attention' },
  Paid: { label: 'Pagado', tone: 'positive' },
  Overdue: { label: 'Vencido', tone: 'danger' },
};

/** Ancho de la barra de utilización, 0–100. Por encima del cupo queda llena: el exceso lo dice el texto. */
export function utilizationFill(utilizationPercent: number | null): number {
  if (utilizationPercent === null || !Number.isFinite(utilizationPercent)) {
    return 0;
  }

  return Math.max(0, Math.min(100, utilizationPercent));
}

/** "37.5%". */
export function formatPercent(value: number | null): string {
  if (value === null) {
    return '—';
  }

  return `${Number.isInteger(value) ? value.toFixed(0) : value.toFixed(1)}%`;
}

/**
 * "Vence en 3 días" · "Vence hoy" · "Venció hace 2 días". Con la fecha y el
 * conteo que manda el backend; aquí no se calcula ningún día.
 */
export function dueLabel(next: Pick<CreditCardNextPayment, 'daysUntilDue' | 'isOverdue'>): string {
  if (next.isOverdue || next.daysUntilDue < 0) {
    const days = Math.abs(next.daysUntilDue);
    return days === 1 ? 'Venció ayer' : `Venció hace ${days} días`;
  }

  if (next.daysUntilDue === 0) {
    return 'Vence hoy';
  }

  return next.daysUntilDue === 1 ? 'Vence mañana' : `Vence en ${next.daysUntilDue} días`;
}

/** "4/12". */
export function installmentProgressLabel(plan: Pick<InstallmentPlan, 'billedInstallments' | 'numberOfInstallments'>): string {
  return `${plan.billedInstallments}/${plan.numberOfInstallments}`;
}

/** "Próxima cuota $100.00 · 15 oct" o, si ya terminó, "Pagada por completo". */
export function nextInstallmentLabel(plan: InstallmentPlan, hidden = false): string {
  if (plan.status === 'Cancelled') {
    return 'Precancelado';
  }

  if (plan.nextInstallmentAmount === null || plan.nextInstallmentClosingDate === null) {
    return 'Todas las cuotas facturadas';
  }

  return `Próxima ${formatCurrency(plan.nextInstallmentAmount, { hidden })} · corte ${formatDueDate(plan.nextInstallmentClosingDate)}`;
}

/** Explica de dónde sale el próximo pago, sin cifras nuevas. */
export function nextPaymentExplanation(next: CreditCardNextPayment): string {
  return next.source === 'statement'
    ? 'Lo que queda por pagar de tu último estado de cuenta.'
    : 'Lo que llevas en el ciclo actual: será el total de tu próximo estado si no hay más movimientos.';
}

/** Lista de días 1–31 para los selectores de corte y pago. */
export const DAY_OPTIONS: number[] = Array.from({ length: 31 }, (_, index) => index + 1);
