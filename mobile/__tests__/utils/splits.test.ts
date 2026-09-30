import {
  centsToInput,
  draftToRequest,
  parseCents,
  splitCategoryLabel,
  summarizeSplitDraft,
  transactionCategoryLabel,
  type SplitDraftLine,
} from '../../utils/splits';

const line = (categoryId: string | null, amountText: string, key = Math.random().toString()): SplitDraftLine => ({
  key,
  categoryId,
  amountText,
  note: '',
});

/**
 * Movimientos divididos: el borrador se valida en centavos enteros y con las
 * mismas reglas que el backend. La prueba principal es el caso real:
 * -$220 → Esposa $70 + Comida $150.
 */
describe('summarizeSplitDraft', () => {
  it('caso real: 70 + 150 de 220 cuadra y se puede guardar', () => {
    const summary = summarizeSplitDraft(220, [line('esposa', '70'), line('comida', '150')]);

    expect(summary.distributedCents).toBe(22000);
    expect(summary.remainingCents).toBe(0);
    expect(summary.overCents).toBe(0);
    expect(summary.canSave).toBe(true);
  });

  it('división parcial: muestra cuánto falta y no deja guardar', () => {
    const summary = summarizeSplitDraft(220, [line('esposa', '70'), line('comida', '100')]);

    expect(summary.remainingCents).toBe(5000);
    expect(summary.canSave).toBe(false);
    expect(summary.blocker).toBe('Falta asignar $50.00.');
  });

  it('te pasaste: lo dice con el monto', () => {
    const summary = summarizeSplitDraft(220, [line('esposa', '100'), line('comida', '150')]);

    expect(summary.overCents).toBe(3000);
    expect(summary.canSave).toBe(false);
    expect(summary.blocker).toBe('Te pasaste por $30.00.');
  });

  it('centavos exactos: 0.10 + 0.20 = 0.30 sin errores de coma flotante', () => {
    const summary = summarizeSplitDraft(0.3, [line('a', '0.10'), line('b', '0.20')]);
    expect(summary.canSave).toBe(true);
  });

  it('rechaza cero, negativos, texto y más de dos decimales', () => {
    for (const bad of ['0', '-5', 'abc', '10.005', '']) {
      const summary = summarizeSplitDraft(220, [line('a', bad), line('b', '220')]);
      expect(summary.canSave).toBe(false);
    }
  });

  it('una sola parte o una categoría repetida no es una división', () => {
    expect(summarizeSplitDraft(220, [line('a', '220')]).canSave).toBe(false);
    expect(summarizeSplitDraft(220, [line('a', '110'), line('a', '110')]).blocker).toBe('Cada categoría puede aparecer una sola vez.');
  });

  it('"Sin categoría" por el resto es una parte válida', () => {
    const summary = summarizeSplitDraft(220, [line('esposa', '70'), line('comida', '100'), line(null, '50')]);
    expect(summary.canSave).toBe(true);
  });
});

describe('montos del formulario', () => {
  it('lee coma o punto y convierte a centavos', () => {
    expect(parseCents('70')).toBe(7000);
    expect(parseCents('70,5')).toBe(7050);
    expect(parseCents('$1,200.99')).toBe(120099);
  });

  it('prellena con el monto restante', () => {
    expect(centsToInput(15000)).toBe('150');
    expect(centsToInput(15050)).toBe('150.50');
    expect(centsToInput(7)).toBe('0.07');
  });

  it('manda montos exactos al backend', () => {
    expect(draftToRequest([line('a', '70.33'), line(null, '149.67')])).toEqual([
      { categoryId: 'a', amount: 70.33, note: null },
      { categoryId: null, amount: 149.67, note: null },
    ]);
  });
});

describe('etiqueta en el listado', () => {
  it('dos partes: "Esposa + Comida"; más: "N categorías"', () => {
    expect(splitCategoryLabel([{ categoryName: 'Esposa' }, { categoryName: 'Comida' }])).toBe('Esposa + Comida');
    expect(splitCategoryLabel([{ categoryName: 'A' }, { categoryName: 'B' }, { categoryName: 'C' }])).toBe('3 categorías');
  });

  it('un movimiento sin división sigue mostrando su categoría', () => {
    expect(transactionCategoryLabel({ isSplit: false, splits: [], categoryName: 'Comida' })).toBe('Comida');
    expect(transactionCategoryLabel({ isSplit: false, splits: [], categoryName: null })).toBe('Sin categoría');
  });
});
