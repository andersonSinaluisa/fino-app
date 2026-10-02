import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Skeleton, Typo } from '../ui';
import { formatCurrency } from '../../utils/format';
import type { BudgetOverview, CommittedMoney, CommittedSourceType, HomeSummary } from '../../types/api';

interface AvailableMoneySectionProps {
  summary: HomeSummary;
  /** GET /finance/committed. `undefined` mientras carga. */
  committed: CommittedMoney | undefined;
  /** GET /budgets (período actual). `undefined` mientras carga. */
  budgets: BudgetOverview | undefined;
  hidden: boolean;
  onOpenCommitted: () => void;
  onOpenBudgets: () => void;
  onOpenBudget: (budgetId: string) => void;
}

const SOURCE_LABEL: Record<CommittedSourceType, string> = {
  reserved_budget: 'Presupuestos reservados',
  upcoming_payment: 'Próximos pagos',
  credit_card: 'Tarjetas',
};

/**
 * "Disponible ahora", armado desde los presupuestos:
 *
 *   Tu dinero
 * − Comprometido   (presupuestos reservados + próximos pagos)
 * = Disponible
 *
 * y debajo, cómo vas con lo que planeaste gastar este mes. Todos los números
 * salen tal cual del backend (CommittedMoneyCalculator y BudgetCalculator):
 * aquí no se suma ni se proyecta nada, solo se muestran y se explican.
 */
export function AvailableMoneySection({
  summary,
  committed,
  budgets,
  hidden,
  onOpenCommitted,
  onOpenBudgets,
  onOpenBudget,
}: AvailableMoneySectionProps) {
  if (summary.accountCount === 0) {
    return null;
  }

  if (!committed) {
    return (
      <View style={styles.wrapper}>
        <Typo variant="caption" color={colors.textSecondary}>
          Disponible ahora
        </Typo>
        <Skeleton height={34} width="55%" />
      </View>
    );
  }

  const money = (value: number) => formatCurrency(value, { hidden });
  const activeBudgets = budgets?.budgets.filter((b) => b.isActive) ?? [];
  const hasBudgets = activeBudgets.length > 0;
  const totals = budgets?.totals;
  const spentShare = totals && totals.budgeted > 0 ? Math.min(totals.spent / totals.budgeted, 1) : 0;
  const insight = budgets?.topInsight ?? null;

  return (
    <View style={styles.wrapper}>
      <Typo variant="caption" color={colors.textSecondary}>
        Disponible ahora
      </Typo>
      <Typo variant="title" tabular>
        {money(committed.available)}
      </Typo>

      <View style={styles.card}>
        <Row label="Tu dinero" value={money(committed.currentMoney)} />

        <Pressable
          onPress={onOpenCommitted}
          accessibilityRole="button"
          accessibilityLabel={`Comprometido ${money(committed.committed)}`}
          accessibilityHint="Muestra de dónde sale el dinero comprometido"
          style={({ pressed }) => [styles.committedBlock, pressed ? styles.pressed : null]}
        >
          <View style={styles.row}>
            <View style={styles.inline}>
              <Typo variant="body" color={colors.textSecondary}>
                Comprometido
              </Typo>
              <Ionicons name="chevron-forward" size={14} color={colors.textSecondary} />
            </View>
            <Typo variant="body" tabular color={colors.textSecondary}>
              {committed.committed > 0 ? `−${money(committed.committed)}` : money(0)}
            </Typo>
          </View>

          {committed.sources.map((source) => (
            <View key={source.type} style={[styles.row, styles.subRow]}>
              <Typo variant="caption" color={colors.textSecondary}>
                {SOURCE_LABEL[source.type]}
              </Typo>
              <Typo variant="caption" color={colors.textSecondary} tabular>
                {money(source.amount)}
              </Typo>
            </View>
          ))}

          {committed.committed === 0 ? (
            <Typo variant="caption" color={colors.textSecondary} style={styles.subRow}>
              Nada comprometido. Activa «Reservar este dinero» en un presupuesto para apartar lo que ya tiene destino.
            </Typo>
          ) : null}
        </Pressable>

        <View style={styles.divider} />
        <Row label="Disponible" value={money(committed.available)} strong />

        {committed.isOvercommitted ? (
          <Pressable onPress={onOpenCommitted} accessibilityRole="alert" style={styles.alert}>
            <Ionicons name="warning-outline" size={16} color={colors.danger} />
            <Typo variant="caption" color={colors.text} style={styles.flex}>
              {hidden
                ? 'Tienes más comprometido de lo que tienes disponible.'
                : `Tienes ${formatCurrency(committed.overcommitted)} más comprometidos de lo que tienes disponible.`}
            </Typo>
          </Pressable>
        ) : null}

        <View style={styles.sectionDivider} />

        {hasBudgets && totals ? (
          <Pressable onPress={onOpenBudgets} accessibilityRole="button" accessibilityHint="Abre Presupuestos" style={styles.budgets}>
            <View style={styles.row}>
              <Typo variant="overline" color={colors.textSecondary}>
                PRESUPUESTOS · {budgets!.label.toUpperCase()}
              </Typo>
              <Ionicons name="chevron-forward" size={14} color={colors.textSecondary} />
            </View>

            <View style={styles.row}>
              <Typo variant="caption" color={colors.textSecondary}>
                Gastado
              </Typo>
              <Typo variant="bodyStrong" tabular>
                {hidden ? '••••' : `${formatCurrency(totals.spent)} de ${formatCurrency(totals.budgeted)}`}
              </Typo>
            </View>

            <View
              style={styles.track}
              accessibilityRole="progressbar"
              accessibilityValue={{ min: 0, max: 100, now: Math.round(spentShare * 100) }}
            >
              <View
                style={[
                  styles.fill,
                  { width: `${spentShare * 100}%`, backgroundColor: totals.exceededCount > 0 ? colors.warning : colors.primary },
                ]}
              />
            </View>

            <View style={styles.row}>
              <Typo variant="caption" color={colors.textSecondary}>
                Restante {money(totals.remaining)}
              </Typo>
              <Typo variant="caption" color={colors.textSecondary}>
                Reservado {money(totals.reserved)}
              </Typo>
            </View>

            {totals.exceededCount > 0 ? (
              <Typo variant="caption" color={colors.danger}>
                {totals.exceededCount === 1 ? '1 presupuesto excedido' : `${totals.exceededCount} presupuestos excedidos`}
              </Typo>
            ) : null}
          </Pressable>
        ) : (
          <Pressable onPress={onOpenBudgets} accessibilityRole="button" style={styles.invite}>
            <Ionicons name="pie-chart-outline" size={18} color={colors.text} />
            <View style={styles.flex}>
              <Typo variant="bodyStrong">Planifica con presupuestos</Typo>
              <Typo variant="caption" color={colors.textSecondary}>
                Define cuánto gastar por categoría y reserva lo que ya tiene destino (alquiler, servicios).
              </Typo>
            </View>
            <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
          </Pressable>
        )}

        {insight ? (
          <Pressable
            onPress={() => onOpenBudget(insight.budgetId)}
            accessibilityRole="button"
            accessibilityHint="Abre el presupuesto"
            style={styles.insight}
          >
            <Ionicons name="bulb-outline" size={16} color={colors.text} />
            <Typo variant="caption" color={colors.text} style={styles.flex}>
              {hidden ? insight.messageWithoutAmounts : insight.message}
            </Typo>
            <Ionicons name="chevron-forward" size={14} color={colors.textSecondary} />
          </Pressable>
        ) : null}
      </View>
    </View>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.row}>
      <Typo variant={strong ? 'bodyStrong' : 'body'} color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular color={colors.text}>
        {value}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  card: {
    marginTop: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  subRow: {
    paddingLeft: spacing.md,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  committedBlock: {
    gap: spacing.xs,
    minHeight: 32,
    justifyContent: 'center',
  },
  pressed: {
    opacity: 0.6,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.xs,
  },
  sectionDivider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.sm,
  },
  alert: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: 'rgba(216, 102, 91, 0.10)',
    borderRadius: radius.md,
    padding: spacing.md,
  },
  budgets: {
    gap: spacing.sm,
  },
  track: {
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
  },
  fill: {
    height: '100%',
    borderRadius: 4,
  },
  invite: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  insight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
    marginTop: spacing.xs,
  },
  flex: {
    flex: 1,
  },
});
