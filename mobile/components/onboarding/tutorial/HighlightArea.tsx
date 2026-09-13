import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, type ViewStyle } from 'react-native';
import { colors, radius } from '../../../theme';
import { useReduceMotion } from '../../../hooks/useReduceMotion';

interface HighlightAreaProps {
  children: React.ReactNode;
  style?: ViewStyle;
  active?: boolean;
}

/**
 * Borde/acento FINO con un pulso suave alrededor de lo que hay que mirar --
 * "highlight → pulse suave → tap" del spec de microinteracciones. Con Reduce
 * Motion activo, el pulso se reemplaza por un borde fijo en vez de un loop:
 * sigue señalando el mismo lugar, solo que sin movimiento.
 */
export function HighlightArea({ children, style, active = true }: HighlightAreaProps) {
  const reduceMotion = useReduceMotion();
  const pulse = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (!active || reduceMotion) {
      pulse.setValue(0);
      return;
    }

    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(pulse, { toValue: 1, duration: 900, useNativeDriver: false }),
        Animated.timing(pulse, { toValue: 0, duration: 900, useNativeDriver: false }),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [active, reduceMotion, pulse]);

  const borderColor = active
    ? pulse.interpolate({ inputRange: [0, 1], outputRange: ['rgba(199, 243, 107, 0.45)', 'rgba(199, 243, 107, 1)'] })
    : 'transparent';

  return (
    <Animated.View
      style={[
        styles.wrap,
        style,
        { borderColor: active ? borderColor : 'transparent', borderWidth: active ? 2 : 0 },
      ]}
    >
      {children}
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    borderRadius: radius.md,
    backgroundColor: colors.surface,
  },
});
