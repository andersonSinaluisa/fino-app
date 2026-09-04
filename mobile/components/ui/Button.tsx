import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { Typo } from './Typo';

type Variant = 'primary' | 'accent' | 'secondary' | 'ghost' | 'danger';

interface ButtonProps {
  label: string;
  onPress: () => void;
  variant?: Variant;
  disabled?: boolean;
  loading?: boolean;
  fullWidth?: boolean;
  compact?: boolean;
  testID?: string;
}

const background: Record<Variant, string> = {
  primary: colors.primary,
  accent: colors.accent,
  secondary: colors.surfaceSecondary,
  ghost: 'transparent',
  danger: colors.danger,
};

const foreground: Record<Variant, string> = {
  primary: colors.onPrimary,
  accent: colors.onAccent,
  secondary: colors.text,
  ghost: colors.textSecondary,
  danger: '#FFFFFF',
};

export function Button({
  label,
  onPress,
  variant = 'primary',
  disabled = false,
  loading = false,
  fullWidth = true,
  compact = false,
  testID,
}: ButtonProps) {
  const inactive = disabled || loading;

  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={() => {
        void Haptics.selectionAsync().catch(() => undefined);
        onPress();
      }}
      style={({ pressed }) => [
        styles.base,
        compact ? styles.compact : null,
        { backgroundColor: background[variant] },
        variant === 'ghost' ? styles.ghost : null,
        fullWidth ? styles.fullWidth : null,
        inactive ? styles.inactive : null,
        pressed && !inactive ? styles.pressed : null,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={foreground[variant]} size="small" />
      ) : (
        <View style={styles.content}>
          <Typo variant="bodyStrong" color={foreground[variant]}>
            {label}
          </Typo>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: 54,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.xl,
  },
  compact: {
    minHeight: 42,
    paddingHorizontal: spacing.lg,
  },
  fullWidth: {
    alignSelf: 'stretch',
  },
  ghost: {
    borderWidth: 1,
    borderColor: colors.borderStrong,
  },
  inactive: {
    opacity: 0.45,
  },
  pressed: {
    opacity: 0.88,
    transform: [{ scale: 0.99 }],
  },
  content: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
});
