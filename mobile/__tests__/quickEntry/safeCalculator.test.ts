import { evaluateExpression, looksLikeExpression, roundToCents } from '../../utils/quickEntry/safeCalculator';

describe('safeCalculator', () => {
  it('resuelve las sumas del §20', () => {
    expect(evaluateExpression('5.50 + 2.25')).toEqual({ value: 7.75, isExpression: true });
    expect(evaluateExpression('10 - 2')).toEqual({ value: 8, isExpression: true });
    expect(evaluateExpression('5 + 2')).toEqual({ value: 7, isExpression: true });
  });

  it('acepta la coma decimal', () => {
    expect(evaluateExpression('5,50 + 2,25')?.value).toBe(7.75);
  });

  it('un número suelto es válido pero no es una expresión', () => {
    expect(evaluateExpression('12.50')).toEqual({ value: 12.5, isExpression: false });
  });

  it('no arrastra el error de coma flotante', () => {
    // 0.1 + 0.2 da 0.30000000000000004 en coma flotante. En un campo de dinero eso
    // se vería, y peor, se guardaría.
    expect(evaluateExpression('0.1 + 0.2')?.value).toBe(0.3);
    expect(roundToCents(0.1 + 0.2)).toBe(0.3);
  });

  it('devuelve null mientras la expresión está a medias', () => {
    expect(evaluateExpression('5 +')).toBeNull();
    expect(evaluateExpression('+')).toBeNull();
    expect(evaluateExpression('')).toBeNull();
  });

  it('rechaza cualquier cosa que no sea aritmética simple', () => {
    // Esto es lo que sustituye a eval(): la gramática no admite nada más, así que
    // no hay nada que ejecutar.
    const attacks = [
      'process.exit(1)',
      '5; console.log(1)',
      'require("fs")',
      '(5+2)*3',
      '5 * 2',
      '5 / 0',
      '__proto__',
      '5 + alert(1)',
    ];

    for (const attack of attacks) {
      expect([attack, evaluateExpression(attack)]).toEqual([attack, null]);
    }
  });

  it('rechaza un resultado negativo en vez de tomar su valor absoluto', () => {
    // El monto es siempre una magnitud; el signo lo pone Gasto/Ingreso (§4).
    expect(evaluateExpression('5 - 8')).toBeNull();
  });

  it('no permite un signo negativo escrito al principio', () => {
    expect(evaluateExpression('-5')).toBeNull();
  });

  it('detecta cuándo hay una operación pendiente', () => {
    expect(looksLikeExpression('5 + 2')).toBe(true);
    expect(looksLikeExpression('5+')).toBe(true);
    expect(looksLikeExpression('5.50')).toBe(false);
  });
});
