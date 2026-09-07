import { StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCompactCurrency } from '../../utils/format';
import type { AnalyticsSeriesPoint } from '../../types/api';

interface WeeklyBarChartProps {
  points: AnalyticsSeriesPoint[];
  hidden: boolean;
}

const CHART_HEIGHT = 96;

/**
 * Ingresos vs. gastos por bucket del periodo elegido -- construido con Views
 * simples, igual que el resto de gráficos hechos a mano en la app
 * (MonthTiles, CategoryBreakdown), en vez de sumar una librería de charts
 * solo para esta pantalla.
 */
export function WeeklyBarChart({ points, hidden }: WeeklyBarChartProps) {
  const max = Math.max(1, ...points.flatMap((p) => [p.income, p.expense]));

  return (
    <View style={styles.card}>
      <View style={styles.legend}>
        <LegendDot color={colors.success} label="Ingresos" />
        <LegendDot color={colors.text} label="Gastos" />
      </View>

      <View style={styles.chart}>
        {points.map((point) => (
          <View key={point.from} style={styles.column}>
            <View style={styles.bars}>
              <View
                style={[
                  styles.bar,
                  { height: Math.max(2, (point.income / max) * CHART_HEIGHT), backgroundColor: colors.success },
                ]}
              />
              <View
                style={[
                  styles.bar,
                  { height: Math.max(2, (point.expense / max) * CHART_HEIGHT), backgroundColor: colors.text },
                ]}
              />
            </View>
            <Typo variant="overline" color={colors.textSecondary} numberOfLines={1} style={styles.columnLabel}>
              {point.label}
            </Typo>
          </View>
        ))}
      </View>

      <View style={styles.totalsRow}>
        <Typo variant="caption" color={colors.textSecondary}>
          Máximo en el periodo: {hidden ? '••••' : formatCompactCurrency(max)}
        </Typo>
      </View>
    </View>
  );
}

function LegendDot({ color, label }: { color: string; label: string }) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.dot, { backgroundColor: color }]} />
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
  legend: {
    flexDirection: 'row',
    gap: spacing.lg,
  },
  legendItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
  chart: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    height: CHART_HEIGHT + 24,
    gap: spacing.xs,
  },
  column: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: spacing.xs,
  },
  bars: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: 3,
    height: CHART_HEIGHT,
  },
  bar: {
    width: 7,
    borderRadius: 3,
  },
  columnLabel: {
    textAlign: 'center',
  },
  totalsRow: {
    alignItems: 'flex-end',
  },
});
