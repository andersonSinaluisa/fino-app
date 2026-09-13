import { useEffect, useRef } from 'react';
import { Animated, Easing, Pressable, StyleSheet } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius } from '../../theme';
import type { VoiceStatus } from '../../hooks/useVoiceEntry';

/**
 * §13: el micrófono dentro de QuickCashEntry. Se mantiene pulsado mientras se
 * habla y se suelta para transcribir -- la misma gramática que un walkie-talkie,
 * que es la que la gente ya conoce de las notas de voz.
 *
 * Mantener pulsado (y no "tocar para empezar / tocar para parar") es también una
 * garantía de §14: el micrófono no puede quedarse abierto por descuido, porque
 * está abierto exactamente mientras hay un dedo encima.
 */

interface VoiceButtonProps {
  status: VoiceStatus;
  onStart: () => void;
  onStop: () => void;
  disabled?: boolean;
}

export function VoiceButton({ status, onStart, onStop, disabled = false }: VoiceButtonProps) {
  const pulse = useRef(new Animated.Value(1)).current;
  const listening = status === 'listening';

  useEffect(() => {
    if (!listening) {
      pulse.setValue(1);
      return;
    }

    // Latido lento mientras escucha: la única señal de que el micrófono está
    // abierto tiene que ser imposible de pasar por alto.
    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(pulse, { toValue: 1.15, duration: 600, easing: Easing.inOut(Easing.quad), useNativeDriver: true }),
        Animated.timing(pulse, { toValue: 1, duration: 600, easing: Easing.inOut(Easing.quad), useNativeDriver: true }),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [listening, pulse]);

  return (
    <Pressable
      testID="voice-button"
      accessibilityRole="button"
      accessibilityLabel="Registrar por voz"
      accessibilityHint="Mantén pulsado y di, por ejemplo, gasté cinco dólares en almuerzo"
      accessibilityState={{ busy: listening || status === 'processing', disabled }}
      disabled={disabled || status === 'unavailable' || status === 'requesting'}
      onPressIn={onStart}
      onPressOut={onStop}
      // Sin retardo: el gesto es "mantener pulsado", así que el micrófono debe
      // abrirse en el mismo instante en que el dedo toca, no 100 ms después.
      unstable_pressDelay={0}
      style={({ pressed }) => [styles.button, pressed ? styles.pressed : null]}
    >
      <Animated.View
        style={[styles.icon, listening ? styles.iconListening : null, { transform: [{ scale: pulse }] }]}
      >
        <Ionicons
          name={listening ? 'mic' : 'mic-outline'}
          size={20}
          color={listening ? colors.onAccent : colors.textSecondary}
        />
      </Animated.View>
    </Pressable>
  );
}

/**
 * El texto de estado de la voz, en el flujo vertical del campo y NO superpuesto.
 *
 * Vive aquí, junto al botón, pero lo pinta `SmartEntryInput` debajo del input: en
 * la fila del input estiraría la altura, y flotando encima -- como estaba -- se
 * encimaba con el enlace "Más detalles", que acababa leyéndose como parte del
 * mensaje de error.
 */
export function voiceStatusText(status: VoiceStatus, partial: string, message: string | null): string | null {
  if (status === 'requesting') {
    return 'Pidiendo permiso...';
  }

  if (status === 'processing') {
    return 'Entendiendo...';
  }

  if (status === 'listening') {
    return partial.length > 0 ? partial : 'Escuchando...';
  }

  return message;
}

const styles = StyleSheet.create({
  button: {
    // §37: 44px reales, aunque el círculo visible sea más pequeño.
    width: 44,
    height: 44,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pressed: {
    opacity: 0.8,
  },
  icon: {
    width: 36,
    height: 36,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceSecondary,
  },
  iconListening: {
    backgroundColor: colors.accent,
  },
});
