import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Card } from '../ui/Card';
import { Typo } from '../ui/Typo';
import { RingChart } from '../ui/RingChart';
import { currentMonthLabel, formatCategoryShare, formatCurrency } from '../../utils/format';
import { buildCategorySlices, buildDonutSegments } from '../../utils/donutChart';
import type { CategoryBreakdownItem } from '../../types/api';

interface CategoryDonutCardProps {
  items: CategoryBreakdownItem[];
  totalExpense: number;
  hidden: boolean;
}

const RING_SIZE = 108;
const RING_STROKE = 20;
const MAX_LEGEND_ROWS = 5;

/**
 * "Gastos por categoría" al inicio de Home: mismo dato que CategoryBreakdown
 * (HomeSummaryDto.categoryBreakdown), pero como anillo -- la vista que pide el
 * usuario para responder de un vistazo "¿en qué se fue mi dinero?" sin entrar
 * a Estadísticas. El anillo agrupa lo que sobra del top 5 en "Otros" (ver
 * utils/donutChart.ts) para no mostrar un círculo incompleto.
 */
export function CategoryDonutCard({ items, totalExpense, hidden }: CategoryDonutCardProps) {
  if (items.length === 0) {
    return (
      <Card>
        <Typo variant="overline" color={colors.textSecondary} style={styles.label}>
          GASTOS POR CATEGORÍA
        </Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Todavía no hay gastos categorizados este mes.
        </Typo>
      </Card>
    );
  }

  const slices = buildCategorySlices(items, colors.borderStrong);
  const segments = buildDonutSegments(slices);
  const legendItems = [...items].sort((a, b) => b.total - a.total).slice(0, MAX_LEGEND_ROWS);
  const otherSlice = slices.find((slice) => slice.id === '__other__');

  return (
    <Card>
      <Typo variant="overline" color={colors.textSecondary} style={styles.label}>
        GASTOS POR CATEGORÍA · {currentMonthLabel().toUpperCase()}
      </Typo>

      <View style={styles.row}>
        <RingChart segments={segments} size={RING_SIZE} strokeWidth={RING_STROKE}>
          <Typo variant="caption" color={colors.textSecondary}>
            Gastado
          </Typo>
          <Typo variant="bodyStrong" tabular numberOfLines={1} style={styles.holeAmount}>
            {hidden ? '••••' : formatCurrency(totalExpense)}
          </Typo>
        </RingChart>

        <View style={styles.legend}>
          {legendItems.map((item) => (
            <LegendRow key={item.categoryId} name={item.name} color={item.color} percentage={item.percentage} />
          ))}

          {otherSlice && otherSlice.percentage > 0 ? (
            <LegendRow name="Otros" color={otherSlice.color} percentage={otherSlice.percentage} />
          ) : null}
        </View>
      </View>
    </Card>
  );
}

function LegendRow({ name, color, percentage }: { name: string; color: string; percentage: number }) {
  return (
    <View style={styles.legendRow}>
      <View style={[styles.dot, { backgroundColor: color }]} />
      <Typo variant="body" numberOfLines={1} style={styles.legendName}>
        {name}
      </Typo>
      <Typo variant="caption" color={colors.textSecondary} tabular>
        {formatCategoryShare(percentage)}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  label: {
    marginBottom: spacing.lg,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.lg,
  },
  holeAmount: {
    maxWidth: RING_SIZE - RING_STROKE * 2 - spacing.xs,
  },
  legend: {
    flex: 1,
    gap: spacing.sm,
  },
  legendRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
  legendName: {
    flex: 1,
  },
});
