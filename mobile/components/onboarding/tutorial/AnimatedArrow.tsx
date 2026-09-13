import { useEffect, useRef } from 'react';
import { Animated, type ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors } from '../../../theme';
import { useReduceMotion } from '../../../hooks/useReduceMotion';

interface AnimatedArrowProps {
  direction?: 'down' | 'right';
  color?: string;
  size?: number;
  style?: ViewStyle;
}

/**
 * Flecha de flujo (banco → archivo → FINO, teléfono → siguiente pantalla,
 * etc.) con un desplazamiento suave en loop -- nunca agresiva, solo un
 * recordatorio de dirección. Con Reduce Motion, queda fija.
 */
export function AnimatedArrow({ direction = 'down', color = colors.textSecondary, size = 22, style }: AnimatedArrowProps) {
  const reduceMotion = useReduceMotion();
  const drift = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (reduceMotion) {
      drift.setValue(0);
      return;
    }

    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(drift, { toValue: 1, duration: 700, useNativeDriver: true }),
        Animated.timing(drift, { toValue: 0, duration: 700, useNativeDriver: true }),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [reduceMotion, drift]);

  const translate = drift.interpolate({ inputRange: [0, 1], outputRange: [0, 6] });
  const transform = direction === 'down' ? [{ translateY: translate }] : [{ translateX: translate }];
  const icon = direction === 'down' ? 'arrow-down' : 'arrow-forward';

  return (
    <Animated.View style={[{ transform: reduceMotion ? [] : transform }, style]}>
      <Ionicons name={icon} size={size} color={color} />
    </Animated.View>
  );
}
