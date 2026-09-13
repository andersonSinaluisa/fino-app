import { createRequestId } from '../../utils/quickEntry/requestId';

/**
 * §36: el identificador de idempotencia viaja a un `Guid?` de .NET. Si el formato
 * no es el canónico, ASP.NET rechaza el CUERPO ENTERO con 400 y el movimiento no
 * se guarda -- que es exactamente lo que pasaba cuando esto se generaba con
 * `crypto.randomUUID`, ausente en Hermes, y caía en un fallback con forma de
 * "timestamp-decimal".
 *
 * Este archivo existe para que ese fallo no pueda repetirse sin que un test lo
 * note.
 */

/** La misma forma que acepta System.Guid: 8-4-4-4-12, hexadecimal en minúsculas. */
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

describe('createRequestId', () => {
  it('genera un GUID con el formato que acepta .NET', () => {
    expect(createRequestId()).toMatch(GUID);
  });

  it('marca versión 4 y variante RFC 4122', () => {
    for (let i = 0; i < 200; i += 1) {
      const id = createRequestId();
      expect(id[14]).toBe('4');
      expect(['8', '9', 'a', 'b']).toContain(id[19]);
    }
  });

  it('no repite identificadores', () => {
    const ids = new Set(Array.from({ length: 2000 }, () => createRequestId()));
    expect(ids.size).toBe(2000);
  });

  it('sigue dando un GUID válido sin crypto.getRandomValues', () => {
    // Hermes sin polyfill: es el caso real que rompía el guardado en el teléfono,
    // no una hipótesis.
    const original = (globalThis as { crypto?: Crypto }).crypto;

    try {
      Object.defineProperty(globalThis, 'crypto', { value: undefined, configurable: true });

      for (let i = 0; i < 200; i += 1) {
        expect(createRequestId()).toMatch(GUID);
      }
    } finally {
      Object.defineProperty(globalThis, 'crypto', { value: original, configurable: true });
    }
  });
});
