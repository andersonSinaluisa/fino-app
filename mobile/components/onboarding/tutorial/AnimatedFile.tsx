import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, View, type ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius } from '../../../theme';
import { Typo } from '../../ui';
import { useReduceMotion } from '../../../hooks/useReduceMotion';

interface AnimatedFileProps {
  /** Nombre corto a mostrar bajo el ícono, p. ej. "movimientos.xlsx". Omitir para solo el ícono. */
  label?: string;
  size?: number;
  style?: ViewStyle;
}

/**
 * El archivo que viaja del banco a FINO -- "Archivo: movimiento vertical
 * suave" del spec de microinteracciones. Pieza reutilizable de la identidad
 * de FINO (Pantalla 2, la animación de archivo dedicada, procesamiento...):
 * este componente es solo el ícono con su bamboleo, quien lo use decide
 * dónde posicionarlo y con qué (AnimatedArrow, TutorialCaption) alrededor.
 */
export function AnimatedFile({ label, size = 40, style }: AnimatedFileProps) {
  const reduceMotion = useReduceMotion();
  const bob = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (reduceMotion) {
      bob.setValue(0);
      return;
    }

    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(bob, { toValue: 1, duration: 1100, useNativeDriver: true }),
        Animated.timing(bob, { toValue: 0, duration: 1100, useNativeDriver: true }),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [reduceMotion, bob]);

  const translateY = bob.interpolate({ inputRange: [0, 1], outputRange: [0, -5] });

  return (
    <View style={[styles.wrap, style]}>
      <Animated.View
        style={[styles.iconWrap, { width: size, height: size }, reduceMotion ? null : { transform: [{ translateY }] }]}
      >
        <Ionicons name="document-text-outline" size={size * 0.6} color={colors.text} />
      </Animated.View>
      {label ? (
        <Typo variant="caption" color={colors.textSecondary}>
          {label}
        </Typo>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    alignItems: 'center',
    gap: 6,
  },
  iconWrap: {
    borderRadius: radius.md,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
