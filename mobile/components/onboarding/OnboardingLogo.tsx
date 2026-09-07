import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui/Typo';

interface OnboardingLogoProps {
  /** Smaller mark used in the Step 2 header, next to the back button. */
  compact?: boolean;
}

/**
 * The app's real wordmark ("Fino"), drawn in the three-node connected-dots
 * mark from the onboarding design system -- the mark's shape and colors come
 * straight from `design/nexo_logo/code.html`, only the label was swapped for
 * the app's actual brand name (everywhere else in the app -- login, splash,
 * profile -- already says "Fino", not "nexo").
 */
export function OnboardingLogo({ compact = false }: OnboardingLogoProps) {
  const box = compact ? 24 : 32;
  const scale = box / 32;

  return (
    <View style={styles.wrap}>
      <View style={{ width: box, height: box }}>
        <View
          style={[
            styles.node,
            {
              width: 14 * scale,
              height: 14 * scale,
              borderRadius: 999,
              backgroundColor: colors.primary,
              left: 2 * scale,
              top: (box - 14 * scale) / 2,
            },
          ]}
        />
        <View
          style={[
            styles.node,
            {
              width: 12 * scale,
              height: 12 * scale,
              borderRadius: 999,
              backgroundColor: colors.accent,
              top: 2 * scale,
              right: 4 * scale,
            },
          ]}
        />
        <View
          style={[
            styles.node,
            {
              width: 10 * scale,
              height: 10 * scale,
              borderRadius: 999,
              backgroundColor: colors.accentSecondary,
              bottom: 2 * scale,
              right: 6 * scale,
            },
          ]}
        />
      </View>
      <Typo style={[styles.word, { fontSize: compact ? 17 : 22 }]}>Fino</Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  node: {
    position: 'absolute',
  },
  word: {
    fontWeight: '800',
    color: colors.text,
    letterSpacing: -0.5,
  },
});
