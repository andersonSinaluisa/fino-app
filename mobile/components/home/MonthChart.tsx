import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { SectionHeader } from '../ui/SectionHeader';
import { Typo } from '../ui/Typo';
import { BalanceSparkline } from '../analytics/BalanceSparkline';
import { formatCurrency, formatMonthComparison } from '../../utils/format';
import type { BalancePoint, MonthComparison } from '../../types/api';

interface MonthChartProps {
  points: BalancePoint[];
  monthExpense: number;
  monthComparison: MonthComparison;
  hidden: boolean;
}

/**
 * Rediseño de Home (2026-09), regla de UX #1: Home tiene EXACTAMENTE un
 * gráfico. Este es ese gráfico -- reutiliza BalanceSparkline tal cual (el
 * mismo componente y los mismos datos reales de AnalyticsDashboard.balanceEvolution
 * que ya se ven en Estadísticas), sin duplicar su lógica de dibujo. Los
 * desgloses por categoría ("En qué gastas") quedaron fuera de Home a propósito:
 * ese análisis vive solo en Estadísticas.
 */
export function MonthChart({ points, monthExpense, monthComparison, hidden }: MonthChartProps) {
  const router = useRouter();

  if (points.length === 0) {
    return null;
  }

  const comparison = formatMonthComparison(monthComparison.expenseChangePercent);
  const percentChange = monthComparison.expenseChangePercent;
  const roundedPercent = percentChange === null ? null : Math.round(Math.abs(percentChange));

  return (
    <View>
      <SectionHeader title="Tu mes" />
      <BalanceSparkline points={points} hidden={hidden} />

      <View style={styles.footer}>
        <Typo variant="caption" color={colors.textSecondary}>
          Gastado {hidden ? '••••' : formatCurrency(monthExpense)}
          {comparison && roundedPercent !== null
            ? ` · vs. mes pasado ${comparison.up ? '+' : '-'}${roundedPercent}%`
            : ''}
        </Typo>

        <Pressable onPress={() => router.push('/(tabs)/estadisticas')} hitSlop={8}>
          <Typo variant="caption" color={colors.text}>
            Ver estadísticas →
          </Typo>
        </Pressable>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  footer: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: spacing.md,
  },
});
