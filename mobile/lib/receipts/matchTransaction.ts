import type { TransactionListItem } from '../../types/api';
import type { ReceiptExtractionResult } from './types';

/**
 * §22-23 y §26: encontrar el movimiento que YA corresponde a esta factura,
 * para no cobrar dos veces el mismo gasto.
 *
 * Hay dos preguntas distintas y se responden con el mismo motor:
 *
 *   1. CONCILIACIÓN (§22): la persona pagó con tarjeta y el banco ya reportó
 *      el movimiento. La factura no debe crear un gasto nuevo, debe
 *      asociarse al que existe.
 *   2. DUPLICADO (§26): la persona ya registró este gasto a mano y ahora
 *      escanea la factura del mismo consumo.
 *
 * No es un sistema nuevo: es el mismo enfoque de puntuación que ya usan
 * WithdrawalDetector e InternalTransferService en el backend -- monto,
 * cercanía temporal y parecido del texto, con umbrales explícitos y sin
 * conciliar nada automáticamente cuando hay ambigüedad.
 */

export const MatchConfidence = {
  High: 'high',
  Medium: 'medium',
  None: 'none',
} as const;

export type MatchConfidenceValue = (typeof MatchConfidence)[keyof typeof MatchConfidence];

export interface TransactionMatch {
  transaction: TransactionListItem;
  score: number;
  confidence: MatchConfidenceValue;
  reasons: MatchReason[];
}

export const MatchReason = {
  ExactAmount: 'exact_amount',
  CloseAmount: 'close_amount',
  SameDate: 'same_date',
  NearDate: 'near_date',
  MerchantSimilar: 'merchant_similar',
} as const;

export type MatchReason = (typeof MatchReason)[keyof typeof MatchReason];

export interface MatchOutcome {
  /** Ordenados de mejor a peor. Vacío si nada pasó el umbral mínimo. */
  candidates: TransactionMatch[];
  /**
   * §23: "no auto-conciliar en casos dudosos". Es true cuando hay varios
   * candidatos prácticamente iguales -- entonces la UI tiene que preguntar en
   * vez de sugerir uno.
   */
  ambiguous: boolean;
  /** El único candidato que se puede sugerir de frente. Null si hay ambigüedad. */
  best: TransactionMatch | null;
}

/** Fuera de esta ventana ni se mira: un cargo tarda días, no semanas. */
const MAX_DAY_GAP = 7;
const HIGH_THRESHOLD = 75;
const MEDIUM_THRESHOLD = 50;
/** Dos candidatos dentro de este margen se consideran empatados. */
const AMBIGUITY_MARGIN = 10;

const DAY_MS = 24 * 60 * 60 * 1000;

export interface MatchOptions {
  /** Para comparar contra "hoy" cuando la factura no trae fecha. */
  now?: Date;
}

export function findReceiptMatches(
  receipt: ReceiptExtractionResult,
  transactions: readonly TransactionListItem[],
  options: MatchOptions = {},
): MatchOutcome {
  const total = receipt.total;

  // Sin total no hay nada que comparar. Emparejar solo por comercio y fecha
  // sería adivinar.
  if (total === null || total <= 0) {
    return { candidates: [], ambiguous: false, best: null };
  }

  const receiptDate = receipt.date ? new Date(`${receipt.date}T12:00:00.000Z`) : (options.now ?? new Date());

  const scored: TransactionMatch[] = [];

  for (const transaction of transactions) {
    const match = scoreTransaction(receipt, total, receiptDate, transaction);
    if (match) {
      scored.push(match);
    }
  }

  scored.sort((a, b) => b.score - a.score);

  const candidates = scored.filter((match) => match.confidence !== MatchConfidence.None);

  if (candidates.length === 0) {
    return { candidates: [], ambiguous: false, best: null };
  }

  // §47: "mismo monto con 3 movimientos el mismo día -> solicitar revisión".
  // Si el segundo candidato está pegado al primero, no hay un ganador honesto.
  const top = candidates[0]!;
  const runnerUp = candidates[1];
  const ambiguous = runnerUp !== undefined && top.score - runnerUp.score < AMBIGUITY_MARGIN;

  return {
    candidates,
    ambiguous,
    best: ambiguous ? null : top,
  };
}

function scoreTransaction(
  receipt: ReceiptExtractionResult,
  total: number,
  receiptDate: Date,
  transaction: TransactionListItem,
): TransactionMatch | null {
  // Una factura es un gasto. Un ingreso nunca puede ser su contraparte, y una
  // pata de transferencia interna tampoco: mover dinero propio no es gastar.
  if (transaction.direction !== 'Expense' || transaction.isInternalTransfer) {
    return null;
  }

  const amountDelta = Math.abs(transaction.amount - total);
  const relative = amountDelta / total;

  // Si el monto no se parece, no hay caso: es la señal más fuerte que hay.
  if (amountDelta > 0.01 && relative > 0.02) {
    return null;
  }

  const transactionDate = new Date(transaction.transactionDate);
  if (Number.isNaN(transactionDate.getTime())) {
    return null;
  }

  const dayGap = Math.abs(
    Math.round((startOfDay(transactionDate) - startOfDay(receiptDate)) / DAY_MS),
  );

  if (dayGap > MAX_DAY_GAP) {
    return null;
  }

  const reasons: MatchReason[] = [];
  let score = 0;

  if (amountDelta <= 0.01) {
    score += 50;
    reasons.push(MatchReason.ExactAmount);
  } else {
    score += 30;
    reasons.push(MatchReason.CloseAmount);
  }

  if (dayGap === 0) {
    score += 25;
    reasons.push(MatchReason.SameDate);
  } else if (dayGap <= 1) {
    score += 18;
    reasons.push(MatchReason.NearDate);
  } else if (dayGap <= 3) {
    score += 8;
    reasons.push(MatchReason.NearDate);
  }

  const similarity = merchantSimilarity(
    receipt.merchantName,
    transaction.merchant ?? transaction.description,
  );

  if (similarity > 0) {
    score += Math.round(similarity * 25);

    if (similarity >= 0.5) {
      reasons.push(MatchReason.MerchantSimilar);
    }
  }

  return {
    transaction,
    score,
    confidence:
      score >= HIGH_THRESHOLD
        ? MatchConfidence.High
        : score >= MEDIUM_THRESHOLD
          ? MatchConfidence.Medium
          : MatchConfidence.None,
    reasons,
  };
}

/**
 * Parecido entre el comercio de la factura y el texto del movimiento, 0..1.
 *
 * Se compara por palabras y no por distancia de edición porque los bancos
 * ecuatorianos escriben cosas como "SUPERMAXI URDESA 001 QUITO": lo que
 * importa es que "SUPERMAXI" aparezca, no que las cadenas se parezcan en
 * conjunto.
 */
export function merchantSimilarity(receiptMerchant: string | null, transactionText: string | null): number {
  const left = tokenize(receiptMerchant);
  const right = tokenize(transactionText);

  if (left.length === 0 || right.length === 0) {
    return 0;
  }

  const rightSet = new Set(right);
  const shared = left.filter((token) => rightSet.has(token)).length;

  return shared / left.length;
}

function tokenize(value: string | null): string[] {
  if (!value) {
    return [];
  }

  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toUpperCase()
    .split(/[^A-Z0-9]+/)
    .filter((token) => token.length >= 3);
}

function startOfDay(date: Date): number {
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
}

/**
 * §26: ¿este gasto ya está registrado a mano?
 *
 * Es la misma búsqueda, restringida a movimientos escritos por la persona
 * (Manual): si el banco ya lo reportó, eso es conciliación (§22) y se maneja
 * con `findReceiptMatches`; si lo escribió ella, es un duplicado.
 */
export function findPossibleDuplicates(
  receipt: ReceiptExtractionResult,
  transactions: readonly TransactionListItem[],
  options: MatchOptions = {},
): TransactionMatch[] {
  const manual = transactions.filter((transaction) => transaction.source === 'Manual');
  return findReceiptMatches(receipt, manual, options).candidates;
}
