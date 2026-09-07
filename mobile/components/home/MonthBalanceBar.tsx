import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCompactCurrency, formatMonthComparison } from '../../utils/format';

interface MonthBalanceBarProps {
  balance: number;
  expenseChangePercent: number | null;
  hidden: boolean;
}

/**
 * Entregable 15 ("Dashboard final MVP"): "Balance del mes" (ingresos - gastos,
 * not to be confused with "Tu dinero" in BalanceHeader, which is the total
 * across every account) plus "comparación mensual" -- both come straight from
 * HomeSummaryDto.Month/MonthComparison, computed by the backend every time, so
 * this line is never blank just because the MonthOverMonth insight didn't win
 * one of the six "Para ti" card slots.
 */
export function MonthBalanceBar({ balance, expenseChangePercent, hidden }: MonthBalanceBarProps) {
  const comparison = formatMonthComparison(expenseChangePercent);
  const positive = balance >= 0;

  return (
    <View style={styles.bar}>
      <View style={styles.row}>
        <Typo variant="caption" color={colors.textSecondary}>
          Balance del mes
        </Typo>
        <Typo variant="bodyStrong" color={positive ? colors.success : colors.warning} tabular>
          {hidden ? '••••' : `${positive ? '+' : '-'}${formatCompactCurrency(Math.abs(balance))}`}
        </Typo>
      </View>

      {comparison ? (
        <View style={styles.comparisonRow}>
          <Ionicons
            name={comparison.up ? 'trending-up' : 'trending-down'}
            size={13}
            color={colors.textSecondary}
          />
          <Typo variant="caption" color={colors.textSecondary}>
            Gastaste {comparison.label}
          </Typo>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.xs,
    marginTop: spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  comparisonRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
