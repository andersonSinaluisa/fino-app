import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui';

interface ConsentCheckboxProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** Texto de la casilla; puede incluir enlaces (Typo con onPress). */
  children: ReactNode;
  accessibilityLabel: string;
}

/**
 * Casilla de consentimiento. Siempre empieza sin marcar (LOPDP art. 8: el
 * consentimiento es una acción de la persona, nunca un valor por defecto).
 */
export function ConsentCheckbox({ checked, onChange, children, accessibilityLabel }: ConsentCheckboxProps) {
  return (
    <Pressable
      onPress={() => onChange(!checked)}
      accessibilityRole="checkbox"
      accessibilityState={{ checked }}
      accessibilityLabel={accessibilityLabel}
      hitSlop={6}
      style={styles.row}
    >
      <View style={[styles.box, checked && styles.boxChecked]}>
        {checked ? <Ionicons name="checkmark" size={16} color={colors.onPrimary} /> : null}
      </View>
      <Typo variant="caption" color={colors.text} style={styles.text}>
        {children}
      </Typo>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  box: {
    width: 22,
    height: 22,
    borderRadius: radius.sm / 2,
    borderWidth: 1.5,
    borderColor: colors.borderStrong,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 1,
  },
  boxChecked: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  text: {
    flex: 1,
  },
});
