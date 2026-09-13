import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import type { AnalyticsSeriesPoint } from '../../types/api';

const CHART_HEIGHT = 112;
const MAX_COLUMNS = 6;

export type IncomeExpenseMode = 'weekly' | 'monthly';

interface IncomeExpenseChartProps {
  points: AnalyticsSeriesPoint[];
  mode: IncomeExpenseMode;
  onModeChange: (mode: IncomeExpenseMode) => void;
  hidden: boolean;
}

/**
 * Estadísticas (rediseño 2026-09): el gráfico protagonista de "Tu mes".
 * Reemplaza tanto el WeeklyBarChart huérfano (ya no lo usaba nadie) como la
 * versión anterior embebida directamente en la pantalla -- mismos datos
 * (AnalyticsDashboard.series / monthlyHistory, vía el selector semanal/mensual
 * que ya existía), pero con una columna seleccionable en vez de una leyenda
 * "Total visible" que no ayudaba a decidir nada.
 */
export function IncomeExpenseChart({ points, mode, onModeChange, hidden }: IncomeExpenseChartProps) {
  const visible = points.slice(-MAX_COLUMNS);
  const [selectedIndex, setSelectedIndex] = useState(visible.length - 1);
  const clampedIndex = Math.min(selectedIndex, Math.max(visible.length - 1, 0));
  const selected = visible[clampedIndex];
  const max = Math.max(1, ...visible.flatMap((point) => [point.income, point.expense]));

  return (
    <View>
      <View style={styles.headerRow}>
        <View style={styles.legendRow}>
          <LegendDot color={colors.success} label="Ingresos" />
          <LegendDot color={colors.primary} label="Gastos" />
        </View>

        <View style={styles.modeSwitch}>
          <ModeButton label="Semanal" selected={mode === 'weekly'} onPress={() => onModeChange('weekly')} />
          <ModeButton label="Mensual" selected={mode === 'monthly'} onPress={() => onModeChange('monthly')} />
        </View>
      </View>

      {selected ? (
        <View style={styles.tooltip}>
          <Typo variant="caption" color={colors.textSecondary}>
            {selected.label}
          </Typo>
          <View style={styles.tooltipRow}>
            <Typo variant="caption" color={colors.success} tabular>
              Ingresos {hidden ? '••••' : formatCurrency(selected.income)}
            </Typo>
            <Typo variant="caption" color={colors.primary} tabular>
              Gastos {hidden ? '••••' : formatCurrency(selected.expense)}
            </Typo>
          </View>
        </View>
      ) : null}

      <View style={styles.chart}>
        {visible.map((point, index) => (
          <Pressable
            key={`${point.from}-${point.label}`}
            accessibilityRole="button"
            accessibilityLabel={`${point.label}: ingresos ${formatCurrency(point.income)}, gastos ${formatCurrency(point.expense)}`}
            onPress={() => setSelectedIndex(index)}
            style={styles.column}
          >
            <View style={styles.barPair}>
              <View
                style={[
                  styles.bar,
                  {
                    height: Math.max(3, (point.income / max) * CHART_HEIGHT),
                    backgroundColor: colors.success,
                    opacity: index === clampedIndex ? 1 : 0.45,
                  },
                ]}
              />
              <View
                style={[
                  styles.bar,
                  {
                    height: Math.max(3, (point.expense / max) * CHART_HEIGHT),
                    backgroundColor: colors.primary,
                    opacity: index === clampedIndex ? 1 : 0.45,
                  },
                ]}
              />
            </View>
            <Typo
              variant="overline"
              color={index === clampedIndex ? colors.text : colors.textSecondary}
              numberOfLines={1}
            >
              {point.label}
            </Typo>
          </Pressable>
        ))}
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

function ModeButton({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={[styles.modeButton, selected ? styles.modeButtonSelected : null]}
    >
      <Typo variant="overline" color={selected ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  legendRow: {
    flexDirection: 'row',
    gap: spacing.md,
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
  modeSwitch: {
    flexDirection: 'row',
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.pill,
    padding: 3,
  },
  modeButton: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: 5,
  },
  modeButtonSelected: {
    backgroundColor: colors.surface,
  },
  tooltip: {
    gap: 2,
    marginBottom: spacing.md,
  },
  tooltipRow: {
    flexDirection: 'row',
    gap: spacing.lg,
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
    gap: spacing.sm,
  },
  barPair: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: 4,
    height: CHART_HEIGHT,
  },
  bar: {
    width: 8,
    borderRadius: 4,
  },
});
