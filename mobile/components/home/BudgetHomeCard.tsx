import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { SectionHeader, Typo } from '../ui';
import { formatCurrency } from '../../utils/format';
import type { BudgetOverview } from '../../types/api';

interface BudgetHomeCardProps {
  overview: BudgetOverview | undefined;
  hidden: boolean;
  onOpenBudgets: () => void;
  onOpenBudget: (budgetId: string) => void;
}

/**
 * Presupuestos en Home, sin saturarla: una línea de resumen y, como mucho,
 * UN insight -- el que el backend eligió como más relevante (y solo si vale la
 * pena, ver BudgetInsightRules.IsHomeWorthy). Sin presupuestos, una invitación
 * discreta en vez de números en cero.
 */
export function BudgetHomeCard({ overview, hidden, onOpenBudgets, onOpenBudget }: BudgetHomeCardProps) {
  if (!overview) {
    return null;
  }

  const active = overview.budgets.filter((b) => b.isActive);

  if (active.length === 0) {
    return (
      <Pressable onPress={onOpenBudgets} accessibilityRole="button" style={styles.invite}>
        <Ionicons name="pie-chart-outline" size={18} color={colors.text} />
        <View style={styles.flex}>
          <Typo variant="bodyStrong">Planifica con presupuestos</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Decide cuánto gastar por categoría y reserva lo que ya tiene destino.
          </Typo>
        </View>
        <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
      </Pressable>
    );
  }

  const { totals, topInsight } = overview;

  return (
    <View>
      <SectionHeader title="Presupuestos" actionLabel="Ver todos" onAction={onOpenBudgets} />
      <View style={styles.card}>
        <Pressable onPress={onOpenBudgets} accessibilityRole="button" style={styles.summaryRow}>
          <Typo variant="body" color={colors.textSecondary}>
            {overview.label}
          </Typo>
          <Typo variant="bodyStrong" tabular>
            {hidden
              ? `${active.length} activo${active.length === 1 ? '' : 's'}`
              : `${formatCurrency(totals.spent)} de ${formatCurrency(totals.budgeted)}`}
          </Typo>
        </Pressable>

        {topInsight ? (
          <Pressable
            onPress={() => onOpenBudget(topInsight.budgetId)}
            accessibilityRole="button"
            accessibilityHint="Abre el presupuesto"
            style={styles.insight}
          >
            <Ionicons name="bulb-outline" size={16} color={colors.text} />
            <Typo variant="body" style={styles.flex}>
              {hidden ? topInsight.messageWithoutAmounts : topInsight.message}
            </Typo>
            <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  invite: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  insight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
