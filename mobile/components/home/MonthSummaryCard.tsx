import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Card } from '../ui/Card';
import { Typo } from '../ui/Typo';
import { currentMonthLabel, formatCurrency, formatMonthComparison } from '../../utils/format';
import type { MonthComparison } from '../../types/api';

interface MonthSummaryCardProps {
  income: number;
  expense: number;
  net: number;
  monthComparison: MonthComparison;
  hidden: boolean;
}

/**
 * Rediseño de Home (2026-09): reemplaza tres piezas separadas (MonthTiles +
 * MonthBalanceBar + el "Balance del mes" repetido en "Para ti") por una sola
 * tarjeta compacta -- mismos datos (HomeSummaryDto.Month/MonthComparison), una
 * sola vez en pantalla.
 */
export function MonthSummaryCard({ income, expense, net, monthComparison, hidden }: MonthSummaryCardProps) {
  const comparison = formatMonthComparison(monthComparison.expenseChangePercent);
  const positive = net >= 0;

  return (
    <Card>
      <Typo variant="overline" color={colors.textSecondary} style={styles.label}>
        {currentMonthLabel().toUpperCase()}
      </Typo>

      <View style={styles.rows}>
        <Row label="Ingresos" value={income} tone={colors.success} sign="+" hidden={hidden} />
        <Row label="Gastos" value={expense} tone={colors.text} sign="-" hidden={hidden} />
        <Row
          label="Balance"
          value={Math.abs(net)}
          tone={positive ? colors.success : colors.warning}
          sign={positive ? '+' : '-'}
          hidden={hidden}
          strong
        />
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
    </Card>
  );
}

function Row({
  label,
  value,
  tone,
  sign,
  hidden,
  strong,
}: {
  label: string;
  value: number;
  tone: string;
  sign: '+' | '-';
  hidden: boolean;
  strong?: boolean;
}) {
  return (
    <View style={styles.row}>
      <Typo variant="body" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular color={tone}>
        {hidden ? '••••' : `${sign}${formatCurrency(value)}`}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  label: {
    marginBottom: spacing.md,
  },
  rows: {
    gap: spacing.sm,
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
    marginTop: spacing.md,
  },
});
