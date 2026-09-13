/**
 * §36: el identificador de idempotencia del guardado rápido.
 *
 * Tiene que ser un **GUID válido**, no un identificador cualquiera: el backend lo
 * recibe como `Guid?` y el binder de ASP.NET rechaza el cuerpo entero con 400 si el
 * texto no tiene la forma canónica. Así se rompía la primera versión, que usaba
 * `globalThis.crypto?.randomUUID?.() ?? \`${Date.now()}-${Math.random()}\``:
 * Hermes no trae `crypto.randomUUID` y `expo-crypto` no está instalado, así que
 * SIEMPRE caía en el fallback y SIEMPRE producía un 400.
 *
 * Se genera aquí, en JavaScript puro, en vez de añadir `expo-crypto`, porque una
 * dependencia nativa obligaría a recompilar la app para algo que no necesita
 * criptografía: esto es una clave de deduplicación, no un secreto.
 */

/** 16 bytes aleatorios, con la mejor fuente de entropía que ofrezca el runtime. */
function randomBytes(): Uint8Array {
  const bytes = new Uint8Array(16);

  const webCrypto = (globalThis as { crypto?: Crypto }).crypto;

  if (typeof webCrypto?.getRandomValues === 'function') {
    webCrypto.getRandomValues(bytes);
    return bytes;
  }

  // Sin CSPRNG (Hermes sin polyfill). `Math.random` basta para lo que esto es: el
  // identificador solo tiene que ser único entre los movimientos manuales de UNA
  // persona, y el índice único es por (UserId, ClientRequestId). Nadie puede
  // adivinar nada útil acertándolo, porque no autoriza nada.
  for (let i = 0; i < bytes.length; i += 1) {
    bytes[i] = Math.floor(Math.random() * 256);
  }

  return bytes;
}

const HEX: readonly string[] = Array.from({ length: 256 }, (_, i) =>
  i.toString(16).padStart(2, '0'),
);

/**
 * UUID versión 4, con el formato canónico 8-4-4-4-12 que espera `System.Guid`.
 */
export function createRequestId(): string {
  const bytes = randomBytes();

  // Los bits que marcan "versión 4" y "variante RFC 4122". Sin ellos sigue siendo
  // un GUID parseable, pero deja de ser un v4 legítimo.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = Array.from(bytes, (byte) => HEX[byte]);

  return [
    hex.slice(0, 4).join(''),
    hex.slice(4, 6).join(''),
    hex.slice(6, 8).join(''),
    hex.slice(8, 10).join(''),
    hex.slice(10, 16).join(''),
  ].join('-');
}
