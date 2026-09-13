import {
  MatchConfidence,
  findPossibleDuplicates,
  findReceiptMatches,
  merchantSimilarity,
} from '../../lib/receipts/matchTransaction';
import { emptyExtraction } from '../../lib/receipts/types';
import type { ReceiptExtractionResult } from '../../lib/receipts/types';
import type { TransactionListItem } from '../../types/api';

function receipt(overrides: Partial<ReceiptExtractionResult> = {}): ReceiptExtractionResult {
  return { ...emptyExtraction(), total: 24.58, date: '2026-09-11', merchantName: 'Supermaxi', ...overrides };
}

let counter = 0;

function transaction(overrides: Partial<TransactionListItem> = {}): TransactionListItem {
  counter += 1;

  return {
    id: `tx-${counter}`,
    financialAccountId: 'acc-1',
    accountAlias: 'Banco Guayaquil',
    providerCode: 'GUAYAQUIL',
    brandColor: '#000000',
    transactionDate: '2026-09-11T15:00:00.000Z',
    amount: 24.58,
    signedAmount: -24.58,
    currency: 'USD',
    direction: 'Expense',
    description: 'SUPERMAXI URDESA',
    merchant: 'SUPERMAXI',
    categoryId: null,
    categoryName: null,
    categoryIcon: null,
    categoryColor: null,
    status: 'Posted',
    source: 'Import',
    isInternalTransfer: false,
    ...overrides,
  } as TransactionListItem;
}

describe('findReceiptMatches (§47)', () => {
  it('mismo monto, misma fecha y mismo comercio: match alto', () => {
    const outcome = findReceiptMatches(receipt(), [transaction()]);

    expect(outcome.best).not.toBeNull();
    expect(outcome.best?.confidence).toBe(MatchConfidence.High);
    expect(outcome.ambiguous).toBe(false);
  });

  it('mismo monto con 20 días de diferencia: no hay match', () => {
    const outcome = findReceiptMatches(receipt(), [
      transaction({ transactionDate: '2026-08-22T15:00:00.000Z' }),
    ]);

    expect(outcome.candidates).toHaveLength(0);
    expect(outcome.best).toBeNull();
  });

  it('mismo monto con tres movimientos el mismo día: pide revisión', () => {
    const outcome = findReceiptMatches(receipt({ merchantName: null }), [
      transaction({ merchant: null, description: 'COMPRA 001' }),
      transaction({ merchant: null, description: 'COMPRA 002' }),
      transaction({ merchant: null, description: 'COMPRA 003' }),
    ]);

    expect(outcome.candidates.length).toBeGreaterThan(1);
    expect(outcome.ambiguous).toBe(true);
    // No se sugiere ninguno: la persona decide.
    expect(outcome.best).toBeNull();
  });

  it('un monto distinto descarta el candidato aunque todo lo demás cuadre', () => {
    const outcome = findReceiptMatches(receipt(), [transaction({ amount: 31.4 })]);

    expect(outcome.candidates).toHaveLength(0);
  });

  it('nunca empareja con un ingreso', () => {
    const outcome = findReceiptMatches(receipt(), [transaction({ direction: 'Income' })]);

    expect(outcome.candidates).toHaveLength(0);
  });

  it('nunca empareja con una pata de transferencia interna', () => {
    // Mover dinero entre cuentas propias no es gastar: un retiro conciliado
    // jamás puede ser la contraparte de una factura.
    const outcome = findReceiptMatches(receipt(), [transaction({ isInternalTransfer: true })]);

    expect(outcome.candidates).toHaveLength(0);
  });

  it('sin total en la factura no intenta emparejar nada', () => {
    const outcome = findReceiptMatches(receipt({ total: null }), [transaction()]);

    expect(outcome.candidates).toHaveLength(0);
  });

  it('un día de diferencia sigue siendo un buen candidato', () => {
    const outcome = findReceiptMatches(receipt(), [
      transaction({ transactionDate: '2026-09-12T09:00:00.000Z' }),
    ]);

    expect(outcome.best?.confidence).toBe(MatchConfidence.High);
  });

  it('el comercio desempata entre dos movimientos del mismo monto y día', () => {
    const outcome = findReceiptMatches(receipt(), [
      transaction({ merchant: 'FARMACIAS CRUZ AZUL', description: 'FARMACIA' }),
      transaction({ merchant: 'SUPERMAXI', description: 'SUPERMAXI URDESA' }),
    ]);

    expect(outcome.ambiguous).toBe(false);
    expect(outcome.best?.transaction.merchant).toBe('SUPERMAXI');
  });
});

describe('findPossibleDuplicates (§26)', () => {
  it('encuentra un gasto que la persona ya registró a mano', () => {
    const duplicates = findPossibleDuplicates(receipt(), [
      transaction({ source: 'Manual', accountAlias: 'Efectivo', providerCode: 'EFECTIVO' }),
    ]);

    expect(duplicates).toHaveLength(1);
  });

  it('no confunde un movimiento del banco con un duplicado manual', () => {
    const duplicates = findPossibleDuplicates(receipt(), [transaction({ source: 'Import' })]);

    expect(duplicates).toHaveLength(0);
  });
});

describe('merchantSimilarity', () => {
  it('reconoce el comercio dentro del texto largo del banco', () => {
    expect(merchantSimilarity('Supermaxi', 'SUPERMAXI URDESA 001 GUAYAQUIL')).toBe(1);
  });

  it('ignora acentos y mayúsculas', () => {
    expect(merchantSimilarity('Farmacía Cruz', 'FARMACIA CRUZ AZUL')).toBe(1);
  });

  it('devuelve 0 cuando no hay nada en común', () => {
    expect(merchantSimilarity('Supermaxi', 'FYBECA')).toBe(0);
  });

  it('devuelve 0 si falta alguno de los dos', () => {
    expect(merchantSimilarity(null, 'SUPERMAXI')).toBe(0);
    expect(merchantSimilarity('Supermaxi', null)).toBe(0);
  });
});
