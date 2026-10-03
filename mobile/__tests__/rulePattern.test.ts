import { normalizeForDisplay, patternFromRange, rangeForPattern, splitWords, toggleWord } from '../utils/rulePattern';

describe('rulePattern', () => {
  const words = splitWords('CELLY AZANZA JIMMY ALEJANDRO BANCO GUAYAQUIL');

  it('finds the suggested pattern as whole words in the description', () => {
    expect(rangeForPattern(words, 'CELLY')).toEqual({ start: 0, end: 0 });
    expect(rangeForPattern(words, 'JIMMY ALEJANDRO')).toEqual({ start: 2, end: 3 });
    expect(rangeForPattern(words, 'CELL')).toBeNull();
    expect(rangeForPattern(words, '')).toBeNull();
  });

  it('keeps the selection contiguous when tapping words', () => {
    let range = toggleWord(null, 0);
    expect(patternFromRange(words, range)).toBe('CELLY');
    range = toggleWord(range, 1);
    expect(patternFromRange(words, range)).toBe('CELLY AZANZA');
    // Tocar una palabra más lejos incluye las del medio: el texto es continuo.
    range = toggleWord(range, 3);
    expect(patternFromRange(words, range)).toBe('CELLY AZANZA JIMMY ALEJANDRO');
    // Un extremo se quita.
    range = toggleWord(range, 0);
    expect(patternFromRange(words, range)).toBe('AZANZA JIMMY ALEJANDRO');
    // En el medio, recorta hasta esa palabra.
    range = toggleWord(range, 2);
    expect(patternFromRange(words, range)).toBe('AZANZA JIMMY');
  });

  it('clears the selection when the only selected word is tapped', () => {
    expect(toggleWord({ start: 2, end: 2 }, 2)).toBeNull();
    expect(patternFromRange(words, null)).toBe('');
  });

  it('approximates the backend normalization when the API does not send it', () => {
    expect(normalizeForDisplay('Celly Azanza  Jimmy, Alejandro — Banco Guayaquil')).toBe('CELLY AZANZA JIMMY ALEJANDRO BANCO GUAYAQUIL');
    expect(normalizeForDisplay('Panadería Ñaño #12')).toBe('PANADERIA NANO 12');
  });
});
