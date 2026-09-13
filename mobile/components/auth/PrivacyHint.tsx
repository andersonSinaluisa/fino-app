import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';

/** Punto 9 del rediseño de Login: transmite privacidad sin prometer "seguridad bancaria". */
export function PrivacyHint() {
  return (
    <View style={styles.row} accessible accessibilityLabel="Tus datos financieros son privados">
      <Ionicons name="lock-closed-outline" size={14} color={colors.textSecondary} />
      <Typo variant="caption" color={colors.textSecondary}>
        Tus datos financieros son privados.
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
    marginTop: spacing.xl,
  },
});
