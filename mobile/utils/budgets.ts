import type { BudgetPeriod, BudgetPriority, BudgetProgress, BudgetUsageLevel } from '../types/api';
import { formatCurrency } from './format';

/**
 * Presupuestos: SOLO presentación. Los números (gastado, restante, nivel,
 * reservado) llegan calculados del backend (BudgetCalculator); aquí nunca se
 * decide un umbral ni se recalcula un monto -- solo se elige cómo decirlo.
 */

export const PERIOD_LABELS: Record<BudgetPeriod, string> = {
  Weekly: 'Semanal',
  Biweekly: 'Quincenal',
  Monthly: 'Mensual',
  Custom: 'Personalizado',
};

export const PRIORITY_LABELS: Record<BudgetPriority, string> = {
  Essential: 'Esencial',
  Important: 'Importante',
  Flexible: 'Flexible',
};

/**
 * El estado nunca depende solo del color: cada nivel tiene su palabra y su
 * ícono, y el mensaje de abajo lo dice con números.
 */
export const LEVEL_PRESENTATION: Record<
  BudgetUsageLevel,
  { label: string; tone: 'positive' | 'neutral' | 'attention' | 'danger'; icon: 'checkmark-circle-outline' | 'alert-circle-outline' | 'warning-outline' | 'close-circle-outline' }
> = {
  Normal: { label: 'En curso', tone: 'positive', icon: 'checkmark-circle-outline' },
  Attention: { label: 'Atención', tone: 'attention', icon: 'alert-circle-outline' },
  NearLimit: { label: 'Cerca del límite', tone: 'attention', icon: 'warning-outline' },
  Exceeded: { label: 'Excedido', tone: 'danger', icon: 'close-circle-outline' },
};

/**
 * "Te quedan $180" · "Has usado el 82%" · "Te quedan $12" · "Excediste tu
 * presupuesto por $24". Con montos ocultos, la versión sin cifras.
 */
export function budgetStatusMessage(progress: BudgetProgress, hidden = false): string {
  switch (progress.level) {
    case 'Exceeded':
      if (progress.overspent <= 0) {
        return 'Llegaste al límite de tu presupuesto';
      }
      return hidden
        ? 'Excediste tu presupuesto'
        : `Excediste tu presupuesto por ${formatCurrency(progress.overspent)}`;
    case 'Attention':
      return `Has usado el ${Math.floor(progress.percentUsed)}%`;
    case 'NearLimit':
    case 'Normal':
    default:
      return hidden ? `Has usado el ${Math.floor(progress.percentUsed)}%` : `Te quedan ${formatCurrency(progress.remaining)}`;
  }
}

/** Ancho de la barra, 0–100. Por encima de 100 la barra queda llena: el exceso lo dice el texto. */
export function progressFill(progress: BudgetProgress): number {
  if (!Number.isFinite(progress.percentUsed) || progress.percentUsed <= 0) {
    return 0;
  }
  return Math.min(progress.percentUsed, 100);
}

/** Lectura completa para lectores de pantalla. */
export function budgetAccessibilityLabel(name: string, progress: BudgetProgress, hidden = false): string {
  const spent = hidden ? '' : `${formatCurrency(progress.spent)} de ${formatCurrency(progress.amount)}, `;
  return `${name}. ${spent}${Math.floor(progress.percentUsed)}% usado. ${LEVEL_PRESENTATION[progress.level].label}. ${budgetStatusMessage(progress, hidden)}.`;
}

/** yyyy-MM-dd en la zona del teléfono, para pedir "este período" / "el anterior". */
export function toLocalDateString(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Un día antes del inicio de una ventana (yyyy-MM-dd): la fecha que pide el período anterior. */
export function dayBefore(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return toLocalDateString(new Date(year!, (month ?? 1) - 1, (day ?? 1) - 1));
}

/** Un día después del fin de una ventana: la fecha que pide el período siguiente. */
export function dayAfter(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return toLocalDateString(new Date(year!, (month ?? 1) - 1, (day ?? 1) + 1));
}

/**
 * El monto que escribe la persona ("300", "12,50", "1,200.00") a número, o
 * null si no es un monto válido. No decide nada financiero: solo lee el input.
 */
export function parseBudgetAmount(text: string): number | null {
  const trimmed = text.trim();
  if (!trimmed) {
    return null;
  }

  let normalized = trimmed.replace(/\s|\$/g, '');
  if (normalized.includes(',') && normalized.includes('.')) {
    normalized = normalized.replace(/,/g, '');
  } else {
    normalized = normalized.replace(',', '.');
  }

  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return null;
  }

  const value = Number(normalized);
  return Number.isFinite(value) && value > 0 ? value : null;
}

/**
 * "20/12/2026" → "2026-12-20". A diferencia de parseDateInput (saldos), aquí
 * una fecha futura es válida: un presupuesto personalizado puede empezar
 * mañana. Rechaza fechas que no existen ("31/02/2026").
 */
export function parseDayInput(value: string): string | null {
  const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(value.trim());
  if (!match) {
    return null;
  }

  const day = Number(match[1]);
  const month = Number(match[2]);
  const year = Number(match[3]);
  const date = new Date(year, month - 1, day);
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) {
    return null;
  }

  return toLocalDateString(date);
}
