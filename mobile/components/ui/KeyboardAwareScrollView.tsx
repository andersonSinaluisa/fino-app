import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from 'react';
import {
  Dimensions,
  Keyboard,
  Platform,
  ScrollView,
  TextInput,
  View,
  type NativeScrollEvent,
  type NativeSyntheticEvent,
  type ScrollViewProps,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useKeyboardInset } from '../../hooks/useKeyboardInset';

/** Aire entre el campo y el borde del teclado (o el de arriba de la pantalla). */
const MARGIN = 24;

interface KeyboardAwareScrollViewProps extends ScrollViewProps {
  /**
   * Agrega al final del contenido un espacio del alto del teclado, para que el
   * último campo pueda subir por encima de él. Apágalo cuando otro elemento ya
   * reserva ese espacio (una hoja inferior con su propio KeyboardSpacer).
   */
  keyboardSpacer?: boolean;
}

/**
 * ScrollView que nunca deja un campo de texto debajo del teclado: al abrirse
 * el teclado (o al pasar a otro campo con él abierto) desplaza lo justo para
 * que el campo enfocado quede visible. Funciona igual en iOS y en Android
 * (edge-to-edge incluido), porque no depende de que el sistema encoja la
 * ventana.
 */
export const KeyboardAwareScrollView = forwardRef<ScrollView, KeyboardAwareScrollViewProps>(function KeyboardAwareScrollView(
  { children, keyboardSpacer = true, onScroll, scrollEventThrottle, keyboardShouldPersistTaps, onTouchEnd, ...rest },
  forwardedRef,
) {
  const scrollRef = useRef<ScrollView>(null);
  useImperativeHandle(forwardedRef, () => scrollRef.current as ScrollView);

  const offsetY = useRef(0);
  const insets = useSafeAreaInsets();
  const keyboardInset = useKeyboardInset();
  const keyboardRef = useRef(0);
  keyboardRef.current = keyboardInset;
  const topRef = useRef(insets.top);
  topRef.current = insets.top;

  const revealFocusedInput = useCallback(() => {
    const scroll = scrollRef.current;
    const input = TextInput.State.currentlyFocusedInput?.() as unknown as View | null;
    if (!scroll || !input || keyboardRef.current === 0 || typeof input.measureInWindow !== 'function') {
      return;
    }

    input.measureInWindow((_x, y, _width, height) => {
      const visibleBottom = Dimensions.get('window').height - keyboardRef.current - MARGIN;
      const visibleTop = topRef.current + MARGIN;
      const bottom = y + height;

      if (bottom > visibleBottom) {
        scroll.scrollTo({ y: offsetY.current + (bottom - visibleBottom), animated: true });
      } else if (y < visibleTop) {
        scroll.scrollTo({ y: Math.max(0, offsetY.current - (visibleTop - y)), animated: true });
      }
    });
  }, []);

  // El teclado se abre (o cambia de alto): espera a que el espacio extra se
  // pinte y recién entonces mide el campo enfocado.
  useEffect(() => {
    if (keyboardInset === 0) {
      return undefined;
    }

    const timer = setTimeout(revealFocusedInput, Platform.OS === 'ios' ? 80 : 40);
    return () => clearTimeout(timer);
  }, [keyboardInset, revealFocusedInput]);

  // Con el teclado ya abierto, pasar a otro campo no siempre vuelve a emitir
  // el evento de "teclado abierto" (Android nunca lo hace): se revisa también
  // cada vez que el teclado se muestra y después de cada toque.
  useEffect(() => {
    const subscription = Keyboard.addListener('keyboardDidShow', () => setTimeout(revealFocusedInput, 40));
    return () => subscription.remove();
  }, [revealFocusedInput]);

  const handleScroll = (event: NativeSyntheticEvent<NativeScrollEvent>) => {
    offsetY.current = event.nativeEvent.contentOffset.y;
    onScroll?.(event);
  };

  return (
    <ScrollView
      ref={scrollRef}
      {...rest}
      keyboardShouldPersistTaps={keyboardShouldPersistTaps ?? 'handled'}
      scrollEventThrottle={scrollEventThrottle ?? 16}
      onScroll={handleScroll}
      onTouchEnd={(event) => {
        onTouchEnd?.(event);
        setTimeout(revealFocusedInput, 250);
      }}
    >
      {children}
      {keyboardSpacer && keyboardInset > 0 ? <View style={{ height: keyboardInset }} /> : null}
    </ScrollView>
  );
});
