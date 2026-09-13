import { pickSpanishLocale } from '../../lib/speech/speechRecognizer';

/**
 * El dictado no funcionaba en iOS porque el idioma estaba hardcodeado a `es-EC`, y
 * el reconocedor de Apple soporta exactamente los mismos idiomas que el dictado del
 * teclado -- donde Ecuador no existe. La documentación de `supportedLocales()` dice
 * que la lista depende del dispositivo, así que la decisión correcta es preguntarle
 * al teléfono. Este archivo fija esa decisión.
 */
describe('pickSpanishLocale', () => {
  it('prefiere Ecuador cuando el dispositivo lo tiene', () => {
    expect(pickSpanishLocale(['en-US', 'es-EC', 'es-ES'])).toBe('es-EC');
  });

  it('cae al español latinoamericano antes que al de España', () => {
    // El caso real de un iPhone: no hay es-EC, pero sí varios españoles.
    expect(pickSpanishLocale(['en-US', 'es-ES', 'es-MX', 'es-419'])).toBe('es-419');
    expect(pickSpanishLocale(['en-US', 'es-ES', 'es-MX'])).toBe('es-MX');
    expect(pickSpanishLocale(['en-US', 'es-ES'])).toBe('es-ES');
  });

  it('acepta cualquier variante de español antes que rendirse', () => {
    expect(pickSpanishLocale(['en-US', 'es-AR'])).toBe('es-AR');
    expect(pickSpanishLocale(['es'])).toBe('es');
  });

  it('tolera guiones bajos y mayúsculas, como los devuelve Android', () => {
    expect(pickSpanishLocale(['es_MX'])).toBe('es_MX');
    expect(pickSpanishLocale(['ES-ec'])).toBe('ES-ec');
  });

  it('devuelve null cuando no hay ningún español', () => {
    // Entonces el sheet avisa una vez, en vez de dejar hablar a la persona diez
    // segundos para nada.
    expect(pickSpanishLocale(['en-US', 'fr-FR', 'pt-BR'])).toBeNull();
  });

  it('devuelve null cuando el dispositivo no supo responder', () => {
    // Android por debajo de API 31 devuelve una lista vacía. No saber qué idiomas
    // hay no es motivo para no intentarlo: se arranca sin forzar idioma.
    expect(pickSpanishLocale([])).toBeNull();
  });

  it('no confunde otros idiomas que empiezan parecido', () => {
    expect(pickSpanishLocale(['et-EE', 'eu-ES'])).toBeNull();
  });
});
