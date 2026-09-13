import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui';
import { OnboardingLogo } from './OnboardingLogo';
import { AnimatedArrow } from './tutorial';
import { useReduceMotion } from '../../hooks/useReduceMotion';

const SOURCES: Array<{ icon: keyof typeof Ionicons.glyphMap; label: string }> = [
  { icon: 'business-outline', label: 'Bancos' },
  { icon: 'wallet-outline', label: 'Billeteras' },
  { icon: 'mail-outline', label: 'Correo' },
];

/**
 * Onboarding funcional, Pantalla 1 ("Bienvenida"): las fuentes REALES que
 * Fino ya sabe leer -- import de estados de cuenta bancarios, billeteras
 * como cuenta manual, movimientos detectados por correo (conectar-correo/) --
 * convergiendo en una sola marca. A propósito no dibuja un saldo inventado
 * (nunca hubo una cuenta todavía en este punto del flujo): "balance" aquí es
 * la idea de un solo lugar, no un número falso.
 */
export function SourcesToFinoAnimation() {
  const reduceMotion = useReduceMotion();
  const sourcesOpacity = useRef(new Animated.Value(0)).current;
  const markOpacity = useRef(new Animated.Value(0)).current;
  const markScale = useRef(new Animated.Value(0.85)).current;

  useEffect(() => {
    if (reduceMotion) {
      sourcesOpacity.setValue(1);
      markOpacity.setValue(1);
      markScale.setValue(1);
      return;
    }

    sourcesOpacity.setValue(0);
    markOpacity.setValue(0);
    markScale.setValue(0.85);

    Animated.sequence([
      Animated.timing(sourcesOpacity, { toValue: 1, duration: 320, useNativeDriver: true }),
      Animated.delay(200),
      Animated.parallel([
        Animated.timing(markOpacity, { toValue: 1, duration: 280, useNativeDriver: true }),
        Animated.spring(markScale, { toValue: 1, useNativeDriver: true, friction: 6 }),
      ]),
    ]).start();
  }, [reduceMotion, sourcesOpacity, markOpacity, markScale]);

  return (
    <View style={styles.wrap}>
      <Animated.View style={[styles.sources, reduceMotion ? undefined : { opacity: sourcesOpacity }]}>
        {SOURCES.map((source) => (
          <View key={source.label} style={styles.source}>
            <View style={styles.sourceIcon}>
              <Ionicons name={source.icon} size={20} color={colors.text} />
            </View>
            <Typo variant="caption" color={colors.textSecondary}>
              {source.label}
            </Typo>
          </View>
        ))}
      </Animated.View>

      <AnimatedArrow direction="down" />

      <Animated.View
        style={[
          styles.mark,
          reduceMotion ? undefined : { opacity: markOpacity, transform: [{ scale: markScale }] },
        ]}
      >
        <OnboardingLogo />
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    alignItems: 'center',
    gap: spacing.lg,
  },
  sources: {
    flexDirection: 'row',
    gap: spacing.xl,
  },
  source: {
    alignItems: 'center',
    gap: spacing.xs,
  },
  sourceIcon: {
    width: 44,
    height: 44,
    borderRadius: radius.md,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  mark: {
    backgroundColor: colors.surface,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.md,
  },
});
