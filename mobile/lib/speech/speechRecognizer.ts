/**
 * §13-15: registro por voz.
 *
 * §14 marca dos condiciones que deciden el diseño de este archivo:
 *
 *   "No activar micrófono sin acción explícita."
 *   "Preferir speech-to-text del dispositivo si está disponible."
 *
 * Por eso el motor es `expo-speech-recognition`, que envuelve SFSpeechRecognizer
 * (iOS) y SpeechRecognizer (Android): el reconocimiento lo hace el sistema
 * operativo del teléfono. Fino no manda audio a ningún servidor propio ni de
 * terceros, y no guarda grabaciones -- lo único que sale del módulo nativo es el
 * texto ya transcrito, que se parsea en el dispositivo y se descarta al cerrar el
 * sheet. Si algún día se usara un servicio externo, §14 obliga a documentarlo, y
 * este es el archivo donde tendría que decirse.
 *
 * El módulo se carga de forma perezosa y tolerante a fallo a propósito. Es un
 * módulo NATIVO: no existe en Expo Go, ni en una build anterior a que se añadiera
 * la dependencia, ni en el entorno de tests. Importarlo arriba con un `import`
 * normal haría que abrir el sheet reventara en todos esos casos. Así, cuando no
 * está, `loadSpeechRecognizer()` devuelve null, el botón de micrófono
 * sencillamente no se dibuja, y el resto del registro rápido funciona igual.
 */

export interface SpeechPermission {
  granted: boolean;
  /** True cuando el sistema ya no volverá a preguntar y hay que ir a Ajustes. */
  canAskAgain: boolean;
  /** True cuando el sistema todavía no ha preguntado nunca. */
  undetermined: boolean;
}

export interface SpeechSession {
  /** Corta la escucha. El resultado final llega por `onResult`. */
  stop: () => void;
  /** Corta y descarta: no habrá resultado. */
  abort: () => void;
}

export interface SpeechRecognizer {
  /** Consulta el estado SIN abrir ningún diálogo. */
  getPermission: () => Promise<SpeechPermission>;
  requestPermission: () => Promise<SpeechPermission>;
  /**
   * El español que este teléfono puede reconocer, o null si no tiene ninguno. Se
   * resuelve una sola vez y se cachea.
   */
  resolveLocale: () => Promise<string | null>;
  /**
   * Empieza a escuchar. `onPartial` va llegando mientras la persona habla (para
   * que la pantalla no parezca congelada) y `onResult`/`onError` llegan una sola
   * vez: la sesión se cierra y se desuscribe sola en cuanto ocurre cualquiera de
   * los dos.
   */
  start: (options: {
    onPartial: (transcript: string) => void;
    onResult: (transcript: string) => void;
    onError: (code: string) => void;
  }) => SpeechSession;
}

/**
 * Orden de preferencia de español, del más cercano a Ecuador al más lejano.
 *
 * NO se elige uno a ciegas. La primera versión hardcodeaba `es-EC` y el dictado
 * fallaba con "language-not-supported" en cualquier iPhone: el reconocedor de Apple
 * soporta exactamente los mismos idiomas que el dictado del teclado, y **Ecuador no
 * está en esa lista**. La documentación de `SFSpeechRecognizer.supportedLocales()`
 * es explícita en que la lista depende del dispositivo y del sistema, así que la
 * única forma correcta es preguntarle al teléfono qué tiene y quedarse con lo mejor
 * que ofrezca.
 */
const PREFERRED_LOCALES = ['es-EC', 'es-419', 'es-US', 'es-MX', 'es-CO', 'es-CL', 'es-ES'];

/** Cualquier variante de español sirve antes que fallar. */
const SPANISH_PREFIX = 'es';

interface NativeSubscription {
  remove: () => void;
}

interface NativeModuleShape {
  getPermissionsAsync: () => Promise<{ granted: boolean; canAskAgain?: boolean; status?: string }>;
  requestPermissionsAsync: () => Promise<{ granted: boolean; canAskAgain?: boolean; status?: string }>;
  getSupportedLocales?: (options: Record<string, unknown>) => Promise<{
    locales: string[];
    installedLocales: string[];
  }>;
  start: (options: Record<string, unknown>) => void;
  stop: () => void;
  abort: () => void;
  addListener: (event: string, handler: (payload: never) => void) => NativeSubscription;
}

/**
 * Elige el mejor español entre los que el dispositivo dice soportar. Exportada para
 * poder probarla sin dispositivo: es donde vive la decisión que rompió el dictado.
 */
export function pickSpanishLocale(supported: readonly string[]): string | null {
  if (supported.length === 0) {
    // El dispositivo no supo responder. Se intenta igual sin forzar idioma: el
    // sistema usará el suyo, que casi siempre es el correcto.
    return null;
  }

  const normalized = new Map(supported.map((locale) => [locale.replace('_', '-').toLowerCase(), locale]));

  for (const preferred of PREFERRED_LOCALES) {
    const match = normalized.get(preferred.toLowerCase());
    if (match) {
      return match;
    }
  }

  // Ninguna de las preferidas: vale cualquier español antes que rendirse.
  for (const [key, original] of normalized) {
    if (key === SPANISH_PREFIX || key.startsWith(`${SPANISH_PREFIX}-`)) {
      return original;
    }
  }

  return null;
}

interface ResultEvent {
  isFinal: boolean;
  results: { transcript: string }[];
}

interface ErrorEvent {
  error: string;
}

let cached: SpeechRecognizer | null | undefined;

/** undefined = todavía sin consultar; null = el dispositivo no ofrece español. */
let resolvedLocale: string | null | undefined;

function loadNativeModule(): NativeModuleShape | null {
  try {
    // require dinámico, no import: tiene que poder fallar sin tumbar el bundle.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const module = require('expo-speech-recognition') as {
      ExpoSpeechRecognitionModule?: NativeModuleShape;
    };

    return module?.ExpoSpeechRecognitionModule ?? null;
  } catch {
    return null;
  }
}

function toPermission(result: {
  granted: boolean;
  canAskAgain?: boolean;
  status?: string;
}): SpeechPermission {
  return {
    granted: result.granted,
    canAskAgain: result.canAskAgain ?? true,
    undetermined: result.status === 'undetermined',
  };
}

/**
 * Devuelve el reconocedor, o null si este build no lo tiene. El resultado se
 * cachea: preguntarlo en cada render de un sheet que se abre muchas veces al día
 * no debe costar un require fallido cada vez.
 */
export function loadSpeechRecognizer(): SpeechRecognizer | null {
  if (cached !== undefined) {
    return cached;
  }

  const native = loadNativeModule();

  if (!native) {
    cached = null;
    return null;
  }

  cached = {
    async getPermission() {
      return toPermission(await native.getPermissionsAsync());
    },

    async requestPermission() {
      // §14: esto solo se llama cuando la persona toca el micrófono por primera
      // vez. Nunca al abrir la app, ni al abrir el sheet. En iOS abre DOS diálogos
      // (reconocimiento de voz y micrófono); en Android, solo RECORD_AUDIO.
      return toPermission(await native.requestPermissionsAsync());
    },

    async resolveLocale() {
      if (resolvedLocale !== undefined) {
        return resolvedLocale;
      }

      try {
        const supported = await native.getSupportedLocales?.({});
        resolvedLocale = pickSpanishLocale(supported?.locales ?? []);
      } catch {
        // Android por debajo de API 31 no sabe responder, y algún servicio puede
        // lanzar. No saber qué idiomas hay no es motivo para no intentarlo: se
        // arranca sin forzar idioma y decide el sistema.
        resolvedLocale = null;
      }

      return resolvedLocale;
    },

    start({ onPartial, onResult, onError }) {
      let settled = false;
      let subscriptions: NativeSubscription[] = [];

      /**
       * Quitar los listeners al terminar NO es opcional. `addListener` acumula:
       * sin esto, la segunda dictada tendría dos juegos de handlers, la tercera
       * tres, y cada resultado se procesaría tantas veces como sesiones hubiera
       * habido.
       */
      const cleanup = () => {
        subscriptions.forEach((subscription) => subscription.remove());
        subscriptions = [];
      };

      const settle = (run: () => void) => {
        if (settled) {
          return;
        }

        settled = true;
        cleanup();
        run();
      };

      subscriptions = [
        native.addListener('result', ((event: ResultEvent) => {
          const transcript = event.results?.[0]?.transcript ?? '';

          if (!event.isFinal) {
            onPartial(transcript);
            return;
          }

          settle(() => onResult(transcript));
        }) as (payload: never) => void),

        native.addListener('error', ((event: ErrorEvent) => {
          settle(() => onError(event?.error ?? 'unknown'));
        }) as (payload: never) => void),

        native.addListener('nomatch', (() => {
          settle(() => onError('no-match'));
        }) as unknown as (payload: never) => void),

        native.addListener('end', (() => {
          // Si el sistema cierra la sesión sin habernos dado un resultado final ni
          // un error, es que no entendió nada. Se trata como "no se entendió", no
          // como un fallo silencioso que dejaría el sheet escuchando para siempre.
          settle(() => onError('no-speech'));
        }) as unknown as (payload: never) => void),
      ];

      try {
        native.start({
          // Cuando `resolvedLocale` es null se omite `lang` a propósito: el sistema
          // usa el idioma del teléfono, que es mejor apuesta que un código que
          // sabemos que no soporta.
          ...(resolvedLocale ? { lang: resolvedLocale } : {}),
          // Resultados parciales para que se vea que Fino está escuchando de verdad.
          interimResults: true,
          // Una frase y para. No es un dictado continuo: es "gasté cinco en almuerzo".
          continuous: false,
          // §14: nada de guardar audio. Explícito, no por omisión.
          recordingOptions: { persist: false },
          // El reconocimiento lo hace el dispositivo cuando puede. `false` deja que
          // el sistema use su servidor si el modelo local no está descargado; es una
          // decisión del sistema operativo, no de Fino, y el usuario la controla
          // desde los ajustes del teléfono.
          requiresOnDeviceRecognition: false,
          addsPunctuation: false,
        });
      } catch (error) {
        settle(() => onError(error instanceof Error ? error.message : 'start-failed'));
      }

      return {
        stop: () => {
          if (settled) {
            return;
          }

          // El resultado final llega después, por el listener de 'result'.
          native.stop();
        },
        abort: () => {
          settle(() => undefined);
          native.abort();
        },
      };
    },
  };

  return cached;
}

/** Para tests: olvida el módulo cacheado. */
export function resetSpeechRecognizerCache(): void {
  cached = undefined;
  resolvedLocale = undefined;
}
