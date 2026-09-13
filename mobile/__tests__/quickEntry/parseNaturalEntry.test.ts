import { parseNaturalEntry, shouldAutoSave } from '../../utils/quickEntry/parseNaturalEntry';

/**
 * §39: las frases exactas que el spec pide cubrir, más los casos donde equivocarse
 * cuesta dinero de verdad (leer una fecha como monto, dar por ingreso un gasto).
 *
 * `NOW` es un MIÉRCOLES a propósito: es el único día desde el que se pueden
 * distinguir las tres lecturas de un nombre de día -- hoy (miércoles), ayer
 * (martes) y "el más reciente hacia atrás" (viernes de la semana pasada).
 */
const NOW = new Date('2026-03-11T17:00:00.000Z'); // miércoles

describe('parseNaturalEntry', () => {
  it('lee "5 almuerzo" como un gasto de 5 con concepto y categoría', () => {
    const result = parseNaturalEntry('5 almuerzo', NOW);

    expect(result.amount).toBe(5);
    expect(result.direction).toBe('Expense');
    expect(result.description).toBe('Almuerzo');
    expect(result.categoryCode).toBe('COMIDA');
    expect(result.occurredAt).toBeNull(); // "ahora"
    expect(result.canSave).toBe(true);
  });

  it('lee "20 gasolina" como transporte', () => {
    const result = parseNaturalEntry('20 gasolina', NOW);

    expect(result.amount).toBe(20);
    expect(result.description).toBe('Gasolina');
    expect(result.categoryCode).toBe('TRANSPORTE');
  });

  it('lee "8 uber ayer" con la fecha de ayer y sin dejarla en el concepto', () => {
    const result = parseNaturalEntry('8 uber ayer', NOW);

    expect(result.amount).toBe(8);
    expect(result.description).toBe('Uber');
    expect(result.categoryCode).toBe('TRANSPORTE');
    expect(result.dateLabel).toBe('Ayer');
    expect(new Date(result.occurredAt!).getUTCDate()).toBe(10);
  });

  it('lee "+50 me pagaron" como ingreso', () => {
    const result = parseNaturalEntry('+50 me pagaron', NOW);

    expect(result.amount).toBe(50);
    expect(result.direction).toBe('Income');
    // "me" y "pagaron" son relleno: lo que queda no es un concepto, y devolver
    // null es más honesto que devolver "Pagaron".
    expect(result.confidence.direction).toBe(1);
  });

  it('acepta todas las formas de escribir un monto del §9', () => {
    const cases: ReadonlyArray<readonly [string, number]> = [
      ['5 cafe', 5],
      ['5.5 cafe', 5.5],
      ['5,50 comida', 5.5],
      ['$5 café', 5],
      ['$ 5 café', 5],
      ['USD 10 transporte', 10],
      ['10 usd transporte', 10],
      ['5 dólares almuerzo', 5],
      ['5 dolares almuerzo', 5],
    ];

    for (const [input, expected] of cases) {
      expect(parseNaturalEntry(input, NOW).amount).toBe(expected);
    }
  });

  it('no confunde un año con un monto', () => {
    // Sin marca de moneda, "2026" es una fecha mal escrita, no dos mil dólares.
    expect(parseNaturalEntry('almuerzo 2026', NOW).amount).toBeNull();
    // Pero con marca de moneda sí lo es.
    expect(parseNaturalEntry('$2026 laptop', NOW).amount).toBe(2026);
  });

  it('interpreta un nombre de día como el más reciente hacia atrás', () => {
    // Hoy es miércoles.
    expect(parseNaturalEntry('10 farmacia miercoles', NOW).dateLabel).toBe('Hoy');
    expect(parseNaturalEntry('10 farmacia martes', NOW).dateLabel).toBe('Ayer');

    // El viernes más reciente es el de hace cinco días, no el que viene.
    const viernes = parseNaturalEntry('10 farmacia viernes', NOW);
    expect(viernes.dateLabel).toBe('Viernes');
    expect(new Date(viernes.occurredAt!) < NOW).toBe(true);
    expect(new Date(viernes.occurredAt!).getUTCDate()).toBe(6);
  });

  it('reconoce farmacia como salud y mercado como supermercado', () => {
    expect(parseNaturalEntry('10 farmacia lunes', NOW).categoryCode).toBe('SALUD');
    expect(parseNaturalEntry('30 mercado efectivo', NOW).categoryCode).toBe('SUPERMERCADO');
  });

  it('deja fuera del concepto la palabra "efectivo"', () => {
    // "30 mercado efectivo" describe el mercado, no un comercio llamado "efectivo".
    expect(parseNaturalEntry('30 mercado efectivo', NOW).description).toBe('Mercado');
  });

  it('entiende las frases habladas del §15', () => {
    const cases: ReadonlyArray<readonly [string, number, string]> = [
      ['Gasté 5 dólares en almuerzo.', 5, 'Expense'],
      ['Gasté seis dólares en almuerzo.', 6, 'Expense'],
      ['Pagué veinte de gasolina.', 20, 'Expense'],
      ['Me dieron cincuenta dólares.', 50, 'Income'],
      ['Gasté ocho en Uber ayer.', 8, 'Expense'],
      ['Recibí cien dólares por freelance.', 100, 'Income'],
    ];

    for (const [input, amount, direction] of cases) {
      const result = parseNaturalEntry(input, NOW);
      expect([input, result.amount]).toEqual([input, amount]);
      expect([input, result.direction]).toEqual([input, direction]);
    }
  });

  it('un verbo de gasto gana a una palabra de ingreso dentro del mismo concepto', () => {
    // "Pagué" dice claramente que sale dinero; "pago" como sustantivo no debe
    // convertirlo en ingreso.
    const result = parseNaturalEntry('Pagué 20 de gasolina', NOW);
    expect(result.direction).toBe('Expense');
    expect(result.categoryCode).toBe('TRANSPORTE');
  });

  it('sin monto reconocible no se puede guardar', () => {
    const result = parseNaturalEntry('almuerzo', NOW);

    expect(result.amount).toBeNull();
    expect(result.canSave).toBe(false);
    // Pero lo que sí entendió no se tira: el concepto sigue ahí para que el sheet
    // lo aproveche en cuanto la persona escriba el número.
    expect(result.description).toBe('Almuerzo');
    expect(result.categoryCode).toBe('COMIDA');
  });

  it('una categoría desconocida se queda en null en vez de inventarse', () => {
    const result = parseNaturalEntry('7 chochos de la esquina', NOW);

    expect(result.amount).toBe(7);
    expect(result.categoryCode).toBeNull();
    // §43: sin categoría se guarda igual. Nunca bloquea.
    expect(result.canSave).toBe(true);
  });

  it('el texto vacío no produce nada que guardar', () => {
    const result = parseNaturalEntry('   ', NOW);

    expect(result.amount).toBeNull();
    expect(result.description).toBeNull();
    expect(result.canSave).toBe(false);
  });

  it('no autoguarda en la primera versión, ni con confianza máxima', () => {
    // §11: "configuración recomendada inicial: NO autoguardar texto natural."
    const perfect = parseNaturalEntry('$8 uber ayer', NOW);

    expect(perfect.confidence.amount).toBe(1);
    expect(shouldAutoSave(perfect)).toBe(false);
  });
});
