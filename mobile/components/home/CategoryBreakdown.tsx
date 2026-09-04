import { StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import type { CategoryBreakdownItem } from '../../types/api';

interface CategoryBreakdownProps {
  items: CategoryBreakdownItem[];
  hidden?: boolean;
  limit?: number;
}

/**
 * Where the money went, as proportional bars. A bar chart beats a donut here:
 * the user is comparing categories against each other, not against a whole.
 */
export function CategoryBreakdown({ items, hidden = false, limit = 5 }: CategoryBreakdownProps) {
  const visible = items.slice(0, limit);
  const max = Math.max(...visible.map((item) => item.total), 1);

  return (
    <View style={styles.card}>
      {visible.map((item, index) => (
        <View key={item.categoryId} style={[styles.row, index > 0 ? styles.spaced : null]}>
          <View style={styles.labelRow}>
            <Typo variant="body" numberOfLines={1} style={styles.name}>
              {item.name}
            </Typo>
            <Typo variant="bodyStrong" tabular>
              {hidden ? '••••' : formatCurrency(item.total)}
            </Typo>
          </View>

          <View style={styles.track}>
            <View
              style={[
                styles.fill,
                { width: `${Math.max(4, (item.total / max) * 100)}%`, backgroundColor: item.color },
              ]}
            />
          </View>

          <Typo variant="caption" color={colors.textSecondary}>
            {item.percentage.toFixed(0)}% · {item.count} {item.count === 1 ? 'movimiento' : 'movimientos'}
          </Typo>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  row: {
    gap: spacing.sm,
  },
  spaced: {
    marginTop: spacing.lg,
  },
  labelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  name: {
    flex: 1,
  },
  track: {
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
  },
  fill: {
    height: 6,
    borderRadius: 3,
  },
});
