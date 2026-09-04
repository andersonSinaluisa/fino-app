import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';

interface BalanceHeaderProps {
  greeting: string;
  name: string;
  total: number;
  accountCount: number;
  estimated: boolean;
  hidden: boolean;
  onToggleHidden: () => void;
}

/**
 * The first thing the user sees. One number, said plainly, with the honesty about
 * whether it is an estimate right underneath it.
 */
export function BalanceHeader({
  greeting,
  name,
  total,
  accountCount,
  estimated,
  hidden,
  onToggleHidden,
}: BalanceHeaderProps) {
  return (
    <View style={styles.wrapper}>
      <View style={styles.topRow}>
        <Typo variant="body" color={colors.textSecondary}>
          {greeting}, {name.split(' ')[0]}
        </Typo>

        <Pressable
          onPress={onToggleHidden}
          hitSlop={12}
          accessibilityRole="button"
          accessibilityLabel={hidden ? 'Mostrar cantidades' : 'Ocultar cantidades'}
          style={styles.eye}
        >
          <Ionicons name={hidden ? 'eye-off-outline' : 'eye-outline'} size={18} color={colors.textSecondary} />
        </Pressable>
      </View>

      <Typo variant="overline" color={colors.textSecondary} style={styles.label}>
        TU DINERO
      </Typo>

      <Typo variant="display" tabular>
        {hidden ? '••••••' : formatCurrency(total)}
      </Typo>

      <Typo variant="caption" color={colors.textSecondary}>
        {accountCount === 1 ? 'En 1 cuenta' : `Entre ${accountCount} cuentas`}
        {estimated ? '  ·  Incluye saldos estimados' : ''}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.lg,
  },
  eye: {
    width: 36,
    height: 36,
    borderRadius: 18,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceSecondary,
  },
  label: {
    marginTop: spacing.sm,
  },
});
