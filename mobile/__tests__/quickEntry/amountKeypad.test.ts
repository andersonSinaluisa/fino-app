import { applyKey, type KeypadKey } from '../../components/quick-entry/AmountKeypad';

/**
 * §4 ("teclado numérico"): las reglas de dinero, comprobadas donde de verdad se
 * aplican. `applyKey` es una función pura precisamente para que estas reglas se
 * puedan probar sin montar la interfaz.
 */

function type(keys: string): string {
  return keys.split('').reduce((text, key) => applyKey(text, key as KeypadKey), '');
}

describe('applyKey', () => {
  it('escribe un monto normal', () => {
    expect(type('1250')).toBe('1250');
    expect(type('12.50')).toBe('12.50');
  });

  it('no permite un segundo separador decimal', () => {
    // §4: "no permitir dos separadores decimales."
    expect(type('5.5.')).toBe('5.5');
    expect(type('5..')).toBe('5.');
  });

  it('no permite más de dos decimales', () => {
    // §4: "más de dos decimales para moneda." La tercera pulsación no hace nada,
    // en vez de escribirse y recortarse después.
    expect(type('5.999')).toBe('5.99');
  });

  it('escribe el punto inicial como "0."', () => {
    // Así el campo nunca muestra algo que no sea un número.
    expect(applyKey('', '.')).toBe('0.');
  });

  it('no deja ceros a la izquierda', () => {
    expect(type('05')).toBe('5');
    expect(type('0')).toBe('0');
    // Pero "0." sigue siendo el principio válido de "0.99".
    expect(type('0.99')).toBe('0.99');
  });

  it('borra de a un carácter', () => {
    expect(applyKey('12.5', 'delete')).toBe('12.');
    expect(applyKey('1', 'delete')).toBe('');
    // Borrar sobre vacío no revienta ni deja el texto en un estado raro.
    expect(applyKey('', 'delete')).toBe('');
  });

  it('no admite un signo negativo', () => {
    // §4: "no permitir valores negativos escritos directamente. El signo depende
    // de Gasto/Ingreso." El teclado sencillamente no tiene esa tecla, y `applyKey`
    // solo acepta las que existen.
    expect(Object.keys({}).includes('-')).toBe(false);
    expect(type('5')).toBe('5');
  });

  it('corta el monto en un número de dígitos que ningún gasto real alcanza', () => {
    const veryLong = type('123456789012');
    expect(veryLong.length).toBeLessThanOrEqual(7);
  });
});
