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
  /** Texto junto al spinner mientras `loading` está activo (p. ej. "Entrando..."). */
  loadingLabel?: string;
  fullWidth?: boolean;
  compact?: boolean;
  testID?: string;
  accessibilityLabel?: string;
  /**
   * Usa la paleta de deshabilitado dedicada (gris cálido + texto gris) en
   * vez de atenuar por opacidad. Opt-in por pantalla para no cambiar el
   * aspecto de los botones ya existentes en el resto de la app.
   */
  softDisabled?: boolean;
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

/**
 * Deshabilitado "de verdad" (formulario inválido, no en carga) para primary:
 * un gris cálido claro con texto gris medio, en vez de atenuar el negro con
 * opacidad -- la queja original era que el botón apagado parecía "una gran
 * masa gris". El resto de variantes conserva el atenuado por opacidad.
 */
const disabledBackground: Partial<Record<Variant, string>> = {
  primary: colors.surfaceSecondary,
};

const disabledForeground: Partial<Record<Variant, string>> = {
  primary: colors.textSecondary,
};

export function Button({
  label,
  onPress,
  variant = 'primary',
  disabled = false,
  loading = false,
  loadingLabel,
  fullWidth = true,
  compact = false,
  testID,
  accessibilityLabel,
  softDisabled = false,
}: ButtonProps) {
  const inactive = disabled || loading;
  // Solo se usa la paleta dedicada cuando está deshabilitado de verdad --
  // mientras carga, el botón se mantiene con su color activo normal.
  const useDisabledPalette = disabled && !loading && softDisabled && disabledBackground[variant] !== undefined;

  const resolvedBackground = useDisabledPalette ? disabledBackground[variant]! : background[variant];
  const resolvedForeground = useDisabledPalette ? disabledForeground[variant]! : foreground[variant];

  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={() => {
        void Haptics.selectionAsync().catch(() => undefined);
        onPress();
      }}
      style={({ pressed }) => [
        styles.base,
        compact ? styles.compact : null,
        { backgroundColor: resolvedBackground },
        variant === 'ghost' ? styles.ghost : null,
        fullWidth ? styles.fullWidth : null,
        inactive && !useDisabledPalette ? styles.inactive : null,
        pressed && !inactive ? styles.pressed : null,
      ]}
    >
      {loading ? (
        <View style={styles.content}>
          <ActivityIndicator color={resolvedForeground} size="small" />
          {loadingLabel ? (
            <Typo variant="bodyStrong" color={resolvedForeground}>
              {loadingLabel}
            </Typo>
          ) : null}
        </View>
      ) : (
        <View style={styles.content}>
          <Typo variant="bodyStrong" color={resolvedForeground}>
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
