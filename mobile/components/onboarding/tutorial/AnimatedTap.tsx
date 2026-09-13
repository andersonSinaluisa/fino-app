import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, type ViewStyle } from 'react-native';
import { colors } from '../../../theme';
import { useReduceMotion } from '../../../hooks/useReduceMotion';

interface AnimatedTapProps {
  style?: ViewStyle;
  size?: number;
}

/**
 * El indicador de "toca aquí": un círculo que se expande y se desvanece,
 * en loop -- "círculo pequeño → expansión → tap → desaparece" del spec.
 * Posicionarlo es responsabilidad de quien lo usa (position: 'absolute' vía
 * `style`), porque solo el llamador sabe sobre qué fila/botón va.
 * Con Reduce Motion, se muestra un punto fijo sin animar en vez del loop.
 */
export function AnimatedTap({ style, size = 28 }: AnimatedTapProps) {
  const reduceMotion = useReduceMotion();
  const progress = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (reduceMotion) {
      progress.setValue(0);
      return;
    }

    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(progress, { toValue: 1, duration: 900, useNativeDriver: true }),
        Animated.delay(400),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [reduceMotion, progress]);

  const scale = progress.interpolate({ inputRange: [0, 1], outputRange: [0.5, 1.6] });
  const opacity = progress.interpolate({ inputRange: [0, 0.3, 1], outputRange: [0.9, 0.6, 0] });

  return (
    <Animated.View
      pointerEvents="none"
      style={[
        styles.ring,
        { width: size, height: size, borderRadius: size / 2 },
        reduceMotion ? styles.static : { transform: [{ scale }], opacity },
        style,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  ring: {
    borderWidth: 2,
    borderColor: colors.accent,
    backgroundColor: 'rgba(199, 243, 107, 0.35)',
  },
  static: {
    opacity: 0.7,
  },
});
