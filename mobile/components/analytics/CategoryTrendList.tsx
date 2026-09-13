import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { Badge } from '../ui/Badge';
import { formatCategoryShare, formatCurrency } from '../../utils/format';
import { iconForCategory } from '../../utils/categoryIcons';
import type { CategoryTrend } from '../../types/api';

interface CategoryTrendListProps {
  items: CategoryTrend[];
  spotlightCategoryId: string | null;
  hidden: boolean;
}

/**
 * Como CategoryBreakdown (barras proporcionales), pero con la comparación
 * contra el periodo anterior que el dashboard de estadísticas necesita --
 * `changePercent` es null cuando la categoría no existía en el periodo
 * anterior, así que esas no muestran una flecha inventada.
 */
export function CategoryTrendList({ items, spotlightCategoryId, hidden }: CategoryTrendListProps) {
  const max = Math.max(...items.map((item) => item.total), 1);

  return (
    <View style={styles.card}>
      {items.map((item, index) => (
        <View key={item.categoryId} style={[styles.row, index > 0 ? styles.spaced : null]}>
          <View style={styles.labelRow}>
            <View style={[styles.iconWrap, { backgroundColor: `${item.color}33` }]}>
              <Ionicons name={iconForCategory(item.icon)} size={15} color={item.color} />
            </View>

            <View style={styles.nameColumn}>
              <View style={styles.nameRow}>
                <Typo variant="body" numberOfLines={1} style={styles.name}>
                  {item.name}
                </Typo>
                {item.categoryId === spotlightCategoryId ? <Badge label="Mayor cambio" tone="attention" /> : null}
              </View>
              <Typo variant="caption" color={colors.textSecondary}>
                {formatCategoryShare(item.percentage)} · {item.count} {item.count === 1 ? 'movimiento' : 'movimientos'}
              </Typo>
            </View>

            <View style={styles.amountColumn}>
              <Typo variant="bodyStrong" tabular>
                {hidden ? '••••' : formatCurrency(item.total)}
              </Typo>
              {item.changePercent !== null ? (
                <View style={styles.changeRow}>
                  <Ionicons
                    name={item.changePercent >= 0 ? 'arrow-up' : 'arrow-down'}
                    size={11}
                    color={item.changePercent >= 0 ? colors.warning : colors.success}
                  />
                  <Typo variant="caption" color={item.changePercent >= 0 ? colors.warning : colors.success}>
                    {Math.abs(Math.round(item.changePercent))}%
                  </Typo>
                </View>
              ) : null}
            </View>
          </View>

          <View style={styles.track}>
            <View
              style={[styles.fill, { width: `${Math.max(4, (item.total / max) * 100)}%`, backgroundColor: item.color }]}
            />
          </View>
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
    gap: spacing.sm,
  },
  iconWrap: {
    width: 32,
    height: 32,
    borderRadius: radius.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  nameColumn: {
    flex: 1,
    gap: 2,
  },
  nameRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  name: {
    flexShrink: 1,
  },
  amountColumn: {
    alignItems: 'flex-end',
    gap: 2,
  },
  changeRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 2,
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
