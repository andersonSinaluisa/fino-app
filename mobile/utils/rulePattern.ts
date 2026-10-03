/**
 * "Categorización personal": qué parte de la descripción del banco usa una
 * regla. El backend compara siempre contra la descripción NORMALIZADA
 * (mayúsculas, sin tildes ni signos, sin números de referencia) y la regla es
 * "contiene este texto". Aquí solo se decide qué palabras seguidas de esa
 * descripción forman el texto -- la validación real (¿es parte de la
 * descripción?, ¿es demasiado genérico?) la hace el backend.
 */

/** Rango de palabras seleccionadas, ambos extremos incluidos. */
export interface WordRange {
  start: number;
  end: number;
}

/**
 * Solo como respaldo cuando el backend todavía no devuelve
 * `normalizedDescription`: una aproximación de su normalización, suficiente
 * para mostrar las palabras.
 */
export function normalizeForDisplay(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

export function splitWords(normalized: string): string[] {
  return normalized.split(' ').filter((word) => word.length > 0);
}

/** Dónde está `pattern` (palabras completas) dentro de `words`; null si no calza. */
export function rangeForPattern(words: string[], pattern: string): WordRange | null {
  const target = splitWords(pattern);
  if (target.length === 0 || target.length > words.length) {
    return null;
  }

  for (let start = 0; start + target.length <= words.length; start++) {
    if (target.every((word, offset) => words[start + offset] === word)) {
      return { start, end: start + target.length - 1 };
    }
  }

  return null;
}

export function patternFromRange(words: string[], range: WordRange | null): string {
  return range ? words.slice(range.start, range.end + 1).join(' ') : '';
}

/**
 * Tocar una palabra:
 * - sin selección: empieza con esa palabra;
 * - fuera del rango: lo extiende hasta ella (el texto siempre es continuo);
 * - en un extremo de un rango de varias palabras: la quita;
 * - la única palabra seleccionada: deja la selección vacía;
 * - en el medio: recorta el rango hasta ella.
 */
export function toggleWord(range: WordRange | null, index: number): WordRange | null {
  if (!range) {
    return { start: index, end: index };
  }

  if (index < range.start) {
    return { start: index, end: range.end };
  }

  if (index > range.end) {
    return { start: range.start, end: index };
  }

  if (range.start === range.end) {
    return null;
  }

  if (index === range.start) {
    return { start: range.start + 1, end: range.end };
  }

  if (index === range.end) {
    return { start: range.start, end: range.end - 1 };
  }

  return { start: range.start, end: index };
}
