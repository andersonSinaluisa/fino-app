import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { OnboardingLogo } from './OnboardingLogo';

interface OnboardingHeaderProps {
  step: 1 | 2;
  /** Present only on Step 2, which can go back to Step 1. */
  onBack?: () => void;
}

export function OnboardingHeader({ step, onBack }: OnboardingHeaderProps) {
  return (
    <View style={styles.row}>
      {onBack ? (
        <Pressable
          onPress={onBack}
          style={styles.backButton}
          hitSlop={8}
          accessibilityRole="button"
          accessibilityLabel="Volver al paso anterior"
        >
          <Ionicons name="arrow-back" size={18} color={colors.text} />
        </Pressable>
      ) : (
        <OnboardingLogo />
      )}

      {onBack ? (
        <View style={styles.centerLogo}>
          <OnboardingLogo compact />
        </View>
      ) : null}

      <View style={styles.stepPill}>
        <View style={styles.stepDot} />
        <Typo style={styles.stepLabel}>Paso {step} de 2</Typo>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  backButton: {
    width: 40,
    height: 40,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  centerLogo: {
    flex: 1,
    alignItems: 'center',
  },
  stepPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    backgroundColor: colors.surfaceSecondary,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
    paddingHorizontal: spacing.md,
    paddingVertical: 7,
    borderRadius: radius.pill,
  },
  stepDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.accent,
  },
  stepLabel: {
    fontSize: 11,
    fontWeight: '600',
    letterSpacing: 0,
    color: colors.textSecondary,
  },
});
