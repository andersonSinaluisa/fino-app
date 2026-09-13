import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Linking } from 'react-native';
import * as Haptics from 'expo-haptics';
import { loadSpeechRecognizer, type SpeechSession } from '../lib/speech/speechRecognizer';
import { AnalyticsEvent, ErrorReason, toTranscriptLengthBucket, track } from '../services/analytics';
import { devLog } from '../services/devLog';

/**
 * §13-15: el estado de la escucha, aislado de la interfaz.
 *
 * El flujo es el del §13: mantener pulsado → "Escuchando..." → soltar →
 * speech-to-text → parser → vista previa. Nunca guarda solo: lo que sale de aquí
 * es un texto que alguien tiene que confirmar (§11).
 *
 * EL PERMISO NO SE PIDE DENTRO DEL GESTO, y esa es la lección de la primera
 * versión, que no funcionaba en iOS: al mantener pulsado el micrófono por primera
 * vez, iOS abría su diálogo de permiso; la persona levantaba el dedo para tocar
 * "Permitir", y ese `onPressOut` llegaba cuando la sesión todavía no existía, así
 * que no paraba nada. Al conceder el permiso, la escucha arrancaba con el botón ya
 * suelto y moría sola en silencio. Desde fuera: se pedía el micrófono y no pasaba
 * nada más.
 *
 * Ahora la primera pulsación solo sirve para pedir el permiso, y se le dice a la
 * persona que ya puede mantener pulsado. Además `holding` sigue al dedo: si la
 * escucha llega a arrancar cuando ya nadie sujeta el botón, se aborta.
 */

export type VoiceStatus = 'idle' | 'requesting' | 'listening' | 'processing' | 'unavailable';

interface UseVoiceEntryOptions {
  /** Se llama con la transcripción final. Aquí es donde el sheet mete el parser. */
  onTranscript: (transcript: string) => void;
}

/** §41: mensajes en lenguaje natural, no códigos del sistema. */
const ERROR_MESSAGES: Readonly<Record<string, string>> = {
  'no-speech': 'No te escuché. Inténtalo otra vez.',
  'no-match': 'No entendí lo que dijiste. Inténtalo otra vez.',
  'audio-capture': 'No pude usar el micrófono.',
  interrupted: 'Se interrumpió la grabación.',
  'not-allowed': 'Fino no tiene permiso para usar el micrófono.',
  'service-not-allowed': 'Tu teléfono no permite el reconocimiento de voz ahora mismo.',
  // Se llega aquí solo si el teléfono de verdad no ofrece NINGÚN español. El
  // mensaje dice qué hacer, no culpa a la persona de un problema que no causó.
  'language-not-supported':
    'Tu teléfono no tiene dictado en español. Actívalo en Ajustes › General › Teclado › Dictado.',
  network: 'El reconocimiento de voz necesita conexión ahora mismo.',
};

function messageFor(code: string): string {
  return ERROR_MESSAGES[code] ?? 'No pude entenderte. Inténtalo otra vez.';
}

export function useVoiceEntry({ onTranscript }: UseVoiceEntryOptions) {
  const recognizer = useMemo(() => loadSpeechRecognizer(), []);
  const [status, setStatus] = useState<VoiceStatus>(recognizer ? 'idle' : 'unavailable');
  const [partial, setPartial] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  const session = useRef<SpeechSession | null>(null);
  /** Sigue al dedo: true entre onPressIn y onPressOut. */
  const holding = useRef(false);
  const granted = useRef(false);

  useEffect(
    () => () => {
      // Desmontar con el micrófono abierto (la persona cerró el sheet a media
      // frase) tiene que cortarlo. Un micrófono encendido que nadie ve es
      // exactamente lo que §14 prohíbe.
      session.current?.abort();
      session.current = null;
    },
    [],
  );

  /**
   * Consulta el permiso al montar, en silencio.
   *
   * `getPermissionsAsync` NO abre ningún diálogo -- solo lee el estado -- así que
   * esto no contradice §14 ("no activar micrófono sin acción explícita"): no toca
   * el micrófono ni pregunta nada, únicamente evita que la primera pulsación tenga
   * que esperar a una llamada nativa. Sin esto, un toque muy corto podía soltar el
   * botón antes de que la sesión existiera y quedarse a medias.
   */
  useEffect(() => {
    if (!recognizer) {
      return;
    }

    let cancelled = false;

    void recognizer
      .getPermission()
      .then((permission) => {
        if (!cancelled && permission.granted) {
          granted.current = true;
          // Consultar los idiomas tampoco abre ningún diálogo ni toca el micrófono.
          void recognizer.resolveLocale().catch(() => undefined);
        }
      })
      .catch(() => undefined);

    return () => {
      cancelled = true;
    };
  }, [recognizer]);

  const finish = useCallback((next: VoiceStatus, text: string | null) => {
    session.current = null;
    setPartial('');
    setStatus(next);
    setMessage(text);
  }, []);

  /**
   * Arranca la escucha de verdad. Solo se llama cuando el permiso YA está
   * concedido, así que entre esta llamada y el micrófono no hay ningún diálogo
   * que pueda robarle el gesto a la persona.
   */
  const listen = useCallback(() => {
    if (!recognizer || session.current) {
      return;
    }

    track(AnalyticsEvent.VoiceEntryStarted);
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => undefined);

    setMessage(null);
    setStatus('listening');

    session.current = recognizer.start({
      onPartial: (transcript) => setPartial(transcript),

      onResult: (transcript) => {
        devLog('voice', 'result', { length: transcript.trim().length });

        if (transcript.trim().length === 0) {
          track(AnalyticsEvent.VoiceEntryFailed, { reason: ErrorReason.Empty });
          finish('idle', messageFor('no-match'));
          return;
        }

        // §38: se registra QUE hubo una transcripción, nunca su contenido.
        // Ni siquiera la longitud exacta: un número de caracteres es una
        // huella del texto dictado. Solo el tramo, que es lo que responde
        // "¿la gente dicta frases cortas o párrafos?".
        track(AnalyticsEvent.VoiceEntryParsed, {
          transcriptLengthBucket: toTranscriptLengthBucket(transcript.trim().length),
        });
        finish('idle', null);
        onTranscript(transcript);
      },

      onError: (code) => {
        devLog('voice', 'error', { code });
        track(AnalyticsEvent.VoiceEntryFailed, { reason: voiceErrorReason(code) });
        // 'aborted' es la persona cancelando: no es un fallo que haya que contarle.
        finish('idle', code === 'aborted' ? null : messageFor(code));
      },
    });

    // La escucha pudo arrancar cuando el dedo ya se había levantado (arranque por
    // el long-press del FAB, o un gesto muy corto). En ese caso se pide el
    // resultado inmediatamente en vez de dejar el micrófono abierto solo.
    if (!holding.current) {
      session.current.stop();
      setStatus('processing');
    }
  }, [finish, onTranscript, recognizer]);

  /** Se llama en onPressIn. Devuelve si de verdad empezó a escuchar. */
  const start = useCallback(async () => {
    if (!recognizer || status === 'listening' || status === 'requesting') {
      return;
    }

    holding.current = true;

    if (granted.current) {
      listen();
      return;
    }

    setStatus('requesting');

    const current = await recognizer.getPermission();
    let permission = current;

    if (!current.granted) {
      // §14: el diálogo se abre AQUÍ, la primera vez que la persona toca el
      // micrófono. Nunca al abrir la app ni al abrir el sheet.
      permission = await recognizer.requestPermission();
    }

    if (!permission.granted) {
      holding.current = false;
      setStatus('idle');
      track(AnalyticsEvent.VoicePermissionDenied);

      Alert.alert(
        'Fino necesita el micrófono',
        'Fino necesita acceso al micrófono para registrar movimientos por voz. La transcripción la hace tu teléfono; Fino no guarda el audio.',
        permission.canAskAgain
          ? [{ text: 'Entendido' }]
          : [
              { text: 'Ahora no', style: 'cancel' },
              { text: 'Abrir ajustes', onPress: () => void Linking.openSettings() },
            ],
      );

      return;
    }

    granted.current = true;

    // Se resuelve el idioma antes de escuchar, no dentro de start(): así, si el
    // teléfono no tiene español, se dice ahora y no después de que la persona haya
    // hablado diez segundos para nada.
    await recognizer.resolveLocale();

    // Si hubo que abrir el diálogo, el dedo ya no está sobre el botón: tocar
    // "Permitir" obliga a levantarlo. Arrancar la escucha aquí sería escuchar sin
    // que nadie sujete nada. Se le dice que ya puede dictar y se espera.
    if (!current.granted) {
      holding.current = false;
      setStatus('idle');
      setMessage('Listo. Mantén pulsado el micrófono y habla.');
      return;
    }

    if (!holding.current) {
      setStatus('idle');
      return;
    }

    listen();
  }, [listen, recognizer, status]);

  /** Se llama en onPressOut. */
  const stop = useCallback(() => {
    holding.current = false;

    if (!session.current) {
      // Todavía se estaba resolviendo el permiso, o nunca llegó a arrancar.
      return;
    }

    setStatus('processing');
    setPartial('');
    session.current.stop();
  }, []);

  const cancel = useCallback(() => {
    holding.current = false;
    session.current?.abort();
    finish(recognizer ? 'idle' : 'unavailable', null);
  }, [finish, recognizer]);

  const dismissMessage = useCallback(() => setMessage(null), []);

  return {
    /** False cuando este build no trae el módulo nativo: el botón no debe dibujarse. */
    available: recognizer !== null,
    status,
    /** Lo que se va entendiendo mientras habla, para que la pantalla no parezca muerta. */
    partial,
    /** Aviso o error en lenguaje natural, o null. */
    message,
    dismissMessage,
    start,
    stop,
    cancel,
  };
}


/**
 * §19: los códigos del motor de voz se traducen a razones de un conjunto
 * cerrado. Un código desconocido cae en 'unknown' en vez de viajar tal cual:
 * analytics no es el sitio donde investigar un error nuevo, para eso está el
 * log de desarrollo.
 */
function voiceErrorReason(code: string): string {
  switch (code) {
    case 'aborted':
      return ErrorReason.Cancelled;
    case 'no-match':
      return ErrorReason.NotRecognized;
    case 'not-allowed':
    case 'service-not-allowed':
      return ErrorReason.PermissionDenied;
    case 'network':
      return ErrorReason.NetworkError;
    case 'audio-capture':
      return ErrorReason.Unknown;
    default:
      return ErrorReason.Unknown;
  }
}
