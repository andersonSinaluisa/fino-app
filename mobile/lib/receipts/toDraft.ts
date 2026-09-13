import { findCategoryCode } from '../../utils/quickEntry/parseNaturalEntry';
import { resolveCategory } from '../../utils/quickEntry/resolveCategory';
import type { Category } from '../../types/api';
import { PaymentHint, type ReceiptExtractionResult } from './types';

/**
 * §36: la factura se convierte en el MISMO borrador que produce el teclado,
 * el texto natural y la voz.
 *
 *   teclado  ─┐
 *   texto ────┤
 *   voz ──────┤
 *   factura ──┤
 *             ↓
 *      ReceiptDraft -> CreateQuickTransactionRequest
 *
 * Aquí no se crea ningún movimiento ni se habla con la API: solo se traduce.
 * Eso es lo que impide que el escaneo acabe siendo un sistema de gastos
 * paralelo.
 */
export interface ReceiptDraft {
  amount: number;
  direction: 'Expense';
  description: string | null;
  categoryId: string | null;
  occurredAt: string | null;
  /** null = decide la pantalla de revisión (efectivo sugerido o cuenta elegida). */
  financialAccountId: string | null;
  /** §21: qué sugerir como cuenta de pago, sin decidirlo por la persona. */
  paymentHint: ReceiptExtractionResult['paymentHint'];
}

export interface ToDraftOptions {
  categories: readonly Category[] | undefined;
  /** La cuenta EFECTIVO, cuando ya existe. */
  cashAccountId: string | null;
}

/**
 * Devuelve null cuando no hay total: sin monto no hay movimiento que crear, y
 * el flujo tiene que pedirlo a mano (§10).
 */
export function receiptToDraft(
  receipt: ReceiptExtractionResult,
  options: ToDraftOptions,
): ReceiptDraft | null {
  if (receipt.total === null || receipt.total <= 0 || !Number.isFinite(receipt.total)) {
    return null;
  }

  // §20: prioridad 3 (palabras del comercio). Las prioridades 1 y 2 -- lo que
  // esta persona ya enseñó y las reglas existentes -- las aplica el servidor
  // cuando se manda sin categoryId, igual que en el registro rápido.
  const hint = findCategoryCode(receipt.merchantName, 'Expense');
  const category = resolveCategory(hint.code, options.categories);

  return {
    amount: receipt.total,
    direction: 'Expense',
    // El comercio es la descripción natural del gasto. Si no se detectó, se
    // manda vacía: el servidor no necesita que le inventemos un texto.
    description: receipt.merchantName,
    categoryId: category?.id ?? null,
    occurredAt: receipt.date ? `${receipt.date}T12:00:00.000Z` : null,
    // §21: solo se preselecciona la cuenta cuando la factura DICE efectivo. Si
    // dice tarjeta no se adivina qué banco fue -- eso lo resuelve la
    // conciliación con el movimiento real, o la persona.
    financialAccountId:
      receipt.paymentHint === PaymentHint.Cash ? options.cashAccountId : null,
    paymentHint: receipt.paymentHint,
  };
}
