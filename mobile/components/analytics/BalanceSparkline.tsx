import { StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCompactCurrency } from '../../utils/format';
import type { BalancePoint } from '../../types/api';

interface BalanceSparklineProps {
  points: BalancePoint[];
  hidden: boolean;
}

const CHART_HEIGHT = 80;

/**
 * Saldo real reconstruido punto a punto (ver AnalyticsService.BuildBalanceEvolutionAsync
 * en el backend) -- nunca una proyección. Se dibuja como columnas normalizadas
 * entre el mínimo y el máximo del periodo, no desde cero, para que las
 * variaciones de un saldo que nunca baja de, digamos, $800 sigan siendo visibles.
 */
export function BalanceSparkline({ points, hidden }: BalanceSparklineProps) {
  if (points.length === 0) {
    return null;
  }

  const values = points.map((p) => p.balance);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;

  const first = points[0]!;
  const last = points[points.length - 1]!;
  const delta = last.balance - first.balance;

  return (
    <View style={styles.card}>
      <View style={styles.headerRow}>
        <Typo variant="caption" color={colors.textSecondary}>
          Saldo al {last.label}
        </Typo>
        <Typo variant="bodyStrong" tabular>
          {hidden ? '••••' : formatCompactCurrency(last.balance)}
        </Typo>
      </View>

      <View style={styles.chart}>
        {points.map((point) => (
          <View key={point.asOf} style={styles.column}>
            <View
              style={[
                styles.bar,
                {
                  height: Math.max(3, ((point.balance - min) / range) * CHART_HEIGHT),
                  backgroundColor: point.balance >= 0 ? colors.accentSecondary : colors.danger,
                },
              ]}
            />
          </View>
        ))}
      </View>

      <View style={styles.footerRow}>
        <Typo variant="caption" color={colors.textSecondary}>
          {first.label}
        </Typo>
        <Typo variant="caption" color={delta >= 0 ? colors.success : colors.warning}>
          {hidden ? '••••' : `${delta >= 0 ? '+' : ''}${formatCompactCurrency(delta)} en el periodo`}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {last.label}
        </Typo>
      </View>
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
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  chart: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    height: CHART_HEIGHT,
    gap: 3,
  },
  column: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'flex-end',
  },
  bar: {
    width: '100%',
    borderRadius: 3,
  },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
});
