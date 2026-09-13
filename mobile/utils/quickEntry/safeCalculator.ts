/**
 * §20 ("calculadora natural"): el campo de monto acepta "5.50 + 2.25" y muestra
 * $7.75.
 *
 * El §20 pide explícitamente NO usar `eval()`, y con razón: `eval` sobre texto que
 * escribe la persona ejecuta cualquier cosa, y en React Native ese "cualquier cosa"
 * corre con acceso al puente nativo. Esto no interpreta código: tokeniza, valida y
 * suma. Lo que no encaje en la gramática de abajo no se evalúa a medias -- se
 * devuelve null y el sheet sigue mostrando lo último válido.
 *
 * La gramática soportada es deliberadamente pequeña (§20: "soportar inicialmente
 * + y -"):
 *
 *     expresión := número (("+" | "-") número)*
 *     número    := dígitos [("." | ",") dígitos]
 *
 * Sin paréntesis, sin signos unarios, sin multiplicación. Multiplicar y dividir se
 * pueden añadir después cambiando solo este archivo; mientras no existan, un "*" es
 * un error de escritura y no una expresión válida a medias.
 */

/** Cuántos sumandos se aceptan antes de asumir que esto ya no es una cuenta mental. */
const MAX_TERMS = 12;

export interface CalculationResult {
  /** El total, redondeado a centavos. */
  value: number;
  /** True cuando de verdad había una operación ("5+2"), no un número suelto ("5"). */
  isExpression: boolean;
}

/**
 * Normaliza un número escrito por una persona en Ecuador.
 *
 * §9 pide aceptar tanto "5.50" como "5,50". Aquí solo puede haber UN separador
 * decimal (la gramática no admite miles: nadie escribe "1.234,56" en un campo de
 * registro rápido), así que la coma y el punto son intercambiables sin ambigüedad.
 */
function parseNumber(raw: string): number | null {
  const normalized = raw.replace(',', '.');

  if (!/^\d*\.?\d*$/.test(normalized) || normalized === '' || normalized === '.') {
    return null;
  }

  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}

/** Redondeo a centavos evitando el clásico 0.1 + 0.2 = 0.30000000000000004. */
export function roundToCents(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

/**
 * Evalúa la expresión, o devuelve null si el texto no es una expresión completa y
 * válida. Null NO significa "cero": significa "todavía no hay nada que mostrar",
 * que es lo correcto mientras alguien está a medio escribir "5 +".
 */
export function evaluateExpression(input: string): CalculationResult | null {
  const text = input.trim();
  if (text.length === 0) {
    return null;
  }

  // Un solo escaneo, carácter a carácter: no hay sustituciones ni expresiones
  // regulares sobre el texto completo que puedan dejar pasar algo inesperado.
  const terms: number[] = [];
  const signs: (1 | -1)[] = [];

  let current = '';
  let pendingSign: 1 | -1 = 1;
  let sawOperator = false;

  const pushTerm = (): boolean => {
    const value = parseNumber(current);
    if (value === null) {
      return false;
    }

    terms.push(value);
    signs.push(pendingSign);
    current = '';
    return true;
  };

  for (const char of text) {
    if (char === ' ') {
      continue;
    }

    if (char === '+' || char === '-') {
      // Un operador solo puede venir DESPUÉS de un número completo. Así "5 + + 2" o
      // un "-5" inicial no cuelan: el signo negativo al principio lo decide el
      // selector Gasto/Ingreso, nunca el teclado (§4).
      if (current.length === 0) {
        return null;
      }

      if (!pushTerm()) {
        return null;
      }

      pendingSign = char === '+' ? 1 : -1;
      sawOperator = true;
      continue;
    }

    if (/[\d.,]/.test(char)) {
      current += char;
      continue;
    }

    // Cualquier otra cosa -- una letra, un paréntesis, un asterisco -- invalida la
    // expresión entera en vez de ignorarse.
    return null;
  }

  if (current.length === 0 || !pushTerm()) {
    return null;
  }

  if (terms.length > MAX_TERMS) {
    return null;
  }

  const total = terms.reduce((sum, term, index) => sum + term * signs[index], 0);

  // Una resta puede dar negativo ("5 - 8"). El monto es siempre una magnitud, así
  // que un total negativo es una expresión que no describe un movimiento: se
  // rechaza en lugar de convertirse en su valor absoluto a espaldas de la persona.
  if (total < 0) {
    return null;
  }

  return { value: roundToCents(total), isExpression: sawOperator };
}

/** True si el texto contiene una operación pendiente de evaluar. */
export function looksLikeExpression(input: string): boolean {
  return /\d\s*[+\-]/.test(input);
}
