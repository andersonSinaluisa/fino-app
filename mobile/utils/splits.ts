import type { TransactionListItem, TransactionSplit } from '../types/api';
import { formatCurrency } from './format';

/**
 * Movimientos divididos: SOLO presentación y validación del borrador. La regla
 * que vale es la del backend (Transaction.Split): aquí solo se evita mandar
 * algo que se sabe que será rechazado y se le dice a la persona cuánto falta.
 *
 * Todo el cálculo va en CENTAVOS enteros: 0.1 + 0.2 en coma flotante no da
 * 0.3, y en una app de dinero "te faltan $0.00000001" no es aceptable.
 */

/** "Esposa + Comida" con dos partes; "3 categorías" con más. */
export function splitCategoryLabel(splits: Pick<TransactionSplit, 'categoryName'>[]): string {
  if (splits.length === 2) {
    return `${splits[0]!.categoryName} + ${splits[1]!.categoryName}`;
  }
  return `${splits.length} categorías`;
}

/** La etiqueta de categoría de una fila de movimientos, dividido o no. */
export function transactionCategoryLabel(
  transaction: Pick<TransactionListItem, 'isSplit' | 'splits' | 'categoryName'>,
): string {
  if (transaction.isSplit && transaction.splits.length > 0) {
    return splitCategoryLabel(transaction.splits);
  }
  return transaction.categoryName ?? 'Sin categoría';
}

/** "70", "70.5", "70,50", "$1,200.00" → centavos. null si no es un monto válido (0, negativo, >2 decimales, texto). */
export function parseCents(text: string): number | null {
  const trimmed = text.trim().replace(/\s|\$/g, '');
  if (!trimmed) {
    return null;
  }

  let normalized = trimmed;
  if (normalized.includes(',') && normalized.includes('.')) {
    normalized = normalized.replace(/,/g, '');
  } else {
    normalized = normalized.replace(',', '.');
  }

  const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(normalized);
  if (!match) {
    return null;
  }

  const cents = Number(match[1]) * 100 + Number((match[2] ?? '').padEnd(2, '0'));
  return Number.isSafeInteger(cents) && cents > 0 ? cents : null;
}

export function toCents(amount: number): number {
  return Math.round(amount * 100);
}

export function fromCents(cents: number): number {
  return cents / 100;
}

/** Para prellenar un campo: "150" o "150.50". */
export function centsToInput(cents: number): string {
  const whole = Math.floor(cents / 100);
  const rest = cents % 100;
  return rest === 0 ? String(whole) : `${whole}.${String(rest).padStart(2, '0')}`;
}

export interface SplitDraftLine {
  key: string;
  categoryId: string | null;
  amountText: string;
  note: string;
}

export interface SplitDraftSummary {
  totalCents: number;
  distributedCents: number;
  /** > 0 = falta asignar. */
  remainingCents: number;
  /** > 0 = te pasaste. */
  overCents: number;
  /** Índices de líneas con monto inválido (vacío, 0, negativo, >2 decimales). */
  invalidLines: number[];
  duplicateCategory: boolean;
  canSave: boolean;
  /** El único motivo, en palabras, por el que todavía no se puede guardar. */
  blocker: string | null;
}

export function summarizeSplitDraft(totalAmount: number, lines: SplitDraftLine[]): SplitDraftSummary {
  const totalCents = toCents(totalAmount);
  const parsed = lines.map((line) => parseCents(line.amountText));
  const invalidLines = parsed.flatMap((cents, index) => (cents === null ? [index] : []));
  const distributedCents = parsed.reduce<number>((sum, cents) => sum + (cents ?? 0), 0);

  const categories = lines.map((line) => line.categoryId ?? '__none__');
  const duplicateCategory = new Set(categories).size !== categories.length;

  const remainingCents = Math.max(totalCents - distributedCents, 0);
  const overCents = Math.max(distributedCents - totalCents, 0);

  let blocker: string | null = null;
  if (lines.length < 2) {
    blocker = 'Agrega al menos dos categorías.';
  } else if (invalidLines.length > 0) {
    blocker = 'Revisa los montos: cada parte debe ser mayor que cero y con máximo dos decimales.';
  } else if (duplicateCategory) {
    blocker = 'Cada categoría puede aparecer una sola vez.';
  } else if (overCents > 0) {
    blocker = `Te pasaste por ${formatCurrency(fromCents(overCents))}.`;
  } else if (remainingCents > 0) {
    blocker = `Falta asignar ${formatCurrency(fromCents(remainingCents))}.`;
  }

  return {
    totalCents,
    distributedCents,
    remainingCents,
    overCents,
    invalidLines,
    duplicateCategory,
    canSave: blocker === null,
    blocker,
  };
}

/** Borrador → petición. Montos en unidades con 2 decimales exactos, derivados de centavos. */
export function draftToRequest(lines: SplitDraftLine[]): { categoryId: string | null; amount: number; note: string | null }[] {
  return lines.map((line) => ({
    categoryId: line.categoryId,
    amount: fromCents(parseCents(line.amountText) ?? 0),
    note: line.note.trim() ? line.note.trim() : null,
  }));
}

/** Tramo del número de partes para analytics: nunca el número exacto ni montos. */
export function partsBucket(count: number): '2' | '3' | '4_plus' {
  if (count <= 2) return '2';
  if (count === 3) return '3';
  return '4_plus';
}
