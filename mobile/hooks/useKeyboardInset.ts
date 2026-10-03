import { useEffect, useState } from 'react';
import { Dimensions, Keyboard, LayoutAnimation, Platform, type KeyboardEvent } from 'react-native';

/**
 * Cuánto tapa el teclado la parte de abajo de la ventana, en puntos.
 *
 * Se calcula con el borde superior del teclado (`screenY`) y no con su alto:
 * si el sistema ya encogió la ventana (Android con adjustResize) el resultado
 * es 0 y nadie suma el teclado dos veces; con edge-to-edge (Android 15+) o en
 * iOS, donde la ventana nunca se encoge, es lo que de verdad queda tapado.
 */
export function keyboardOverlap(event: KeyboardEvent): number {
  const top = event.endCoordinates.screenY;
  if (!Number.isFinite(top) || top <= 0) {
    return Math.max(0, Math.round(event.endCoordinates.height));
  }

  return Math.max(0, Math.round(Dimensions.get('window').height - top));
}

const SHOW_EVENT = Platform.OS === 'ios' ? 'keyboardWillChangeFrame' : 'keyboardDidShow';
const HIDE_EVENT = Platform.OS === 'ios' ? 'keyboardWillHide' : 'keyboardDidHide';

function animateWith(event: KeyboardEvent | null) {
  // iOS: acompaña la curva y la duración del teclado, igual que
  // KeyboardAvoidingView. En Android el evento llega cuando el teclado ya
  // terminó de abrir, así que el cambio va directo.
  if (Platform.OS !== 'ios' || !event?.duration) {
    return;
  }

  LayoutAnimation.configureNext({
    duration: event.duration,
    update: { duration: event.duration, type: LayoutAnimation.Types.keyboard },
  });
}

/** Alto que el teclado tapa ahora mismo (0 si está cerrado). */
export function useKeyboardInset(): number {
  const [inset, setInset] = useState(0);

  useEffect(() => {
    let current = 0;
    const apply = (next: number, event: KeyboardEvent | null) => {
      if (current === next) {
        return;
      }

      current = next;
      animateWith(event);
      setInset(next);
    };

    const subscriptions = [
      Keyboard.addListener(SHOW_EVENT, (event) => apply(keyboardOverlap(event), event)),
      Keyboard.addListener(HIDE_EVENT, (event) => apply(0, event)),
    ];

    return () => subscriptions.forEach((subscription) => subscription.remove());
  }, []);

  return inset;
}
