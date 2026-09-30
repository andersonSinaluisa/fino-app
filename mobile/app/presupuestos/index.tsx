import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { BudgetRow } from '../../components/budgets/BudgetRow';
import { BudgetFormSheet } from '../../components/budgets/BudgetFormSheet';
import { useBudgets } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { AnalyticsSource } from '../../services/analytics';
import { formatCurrency } from '../../utils/format';
import { dayAfter, dayBefore } from '../../utils/budgets';
import { reportExceededOnce } from '../../components/budgets/exceededTracking';

/**
 * Presupuestos: "¿Cuánto planeé gastar y cómo voy?". Todo lo que se ve aquí
 * llega calculado del backend (GET /budgets); esta pantalla solo lo ordena.
 * Las flechas del encabezado piden otro mes -- los períodos pasados se
 * reconstruyen desde los movimientos, nunca se borran.
 */
export default function BudgetsScreen() {
  const router = useRouter();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const [date, setDate] = useState<string | undefined>(undefined);
  const [formOpen, setFormOpen] = useState(false);
  const { data, isLoading, isError, refetch, isRefetching } = useBudgets(date);

  useEffect(() => {
    if (!data) {
      return;
    }
    for (const budget of data.budgets) {
      if (budget.isActive && budget.progress?.level === 'Exceeded' && budget.progress.isCurrentWindow && budget.window) {
        reportExceededOnce(budget.id, budget.window.start, budget.period);
      }
    }
  }, [data]);

  const money = (value: number) => formatCurrency(value, { hidden });
  const firstOfMonth = data ? `${data.date.slice(0, 8)}01` : undefined;
  const lastOfMonth = data ? lastDayOfMonth(data.date) : undefined;

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back} accessibilityRole="button" accessibilityLabel="Volver">
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Presupuestos</Typo>
        <View style={styles.monthRow}>
          <Pressable
            onPress={() => firstOfMonth && setDate(dayBefore(firstOfMonth))}
            hitSlop={12}
            accessibilityRole="button"
            accessibilityLabel="Mes anterior"
          >
            <Ionicons name="chevron-back-circle-outline" size={24} color={colors.textSecondary} />
          </Pressable>
          <Typo variant="subheading" color={colors.textSecondary}>
            {data?.label ?? ' '}
          </Typo>
          <Pressable
            onPress={() => lastOfMonth && setDate(dayAfter(lastOfMonth))}
            hitSlop={12}
            accessibilityRole="button"
            accessibilityLabel="Mes siguiente"
          >
            <Ionicons name="chevron-forward-circle-outline" size={24} color={colors.textSecondary} />
          </Pressable>
          {data && !data.isCurrent ? (
            <Pressable onPress={() => setDate(undefined)} accessibilityRole="button" style={styles.todayChip}>
              <Typo variant="caption">Hoy</Typo>
            </Pressable>
          ) : null}
        </View>
      </View>

      {isLoading || !data ? (
        isError ? (
          <EmptyState
            icon="cloud-offline-outline"
            title="No pudimos cargar tus presupuestos"
            actionLabel="Reintentar"
            onAction={() => void refetch()}
          />
        ) : (
          <View style={styles.stack}>
            <SkeletonCard />
            <SkeletonCard />
          </View>
        )
      ) : data.budgets.length === 0 ? (
        <EmptyState
          icon="pie-chart-outline"
          title={data.isCurrent ? 'Planifica tu dinero' : 'Sin presupuestos en este mes'}
          body="Define cuánto quieres gastar por categoría y, si quieres, reserva el dinero de lo que ya tiene destino (alquiler, servicios)."
          actionLabel="Crear presupuesto"
          onAction={() => setFormOpen(true)}
        />
      ) : (
        <>
          <View style={styles.summary} accessibilityRole="summary">
            <SummaryTile label="Presupuestado" value={money(data.totals.budgeted)} />
            <SummaryTile label="Gastado" value={money(data.totals.spent)} />
            <SummaryTile label="Restante" value={money(data.totals.remaining)} />
            <SummaryTile label="Reservado" value={money(data.totals.reserved)} />
          </View>

          {data.topInsight ? (
            <Pressable
              onPress={() => router.push({ pathname: '/presupuestos/[id]', params: { id: data.topInsight!.budgetId, source: AnalyticsSource.Budgets } })}
              accessibilityRole="button"
              style={styles.insight}
            >
              <Ionicons name="bulb-outline" size={18} color={colors.text} />
              <Typo variant="body" style={styles.flex}>
                {hidden ? data.topInsight.messageWithoutAmounts : data.topInsight.message}
              </Typo>
              <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
            </Pressable>
          ) : null}

          <View style={styles.stack}>
            {data.budgets.map((budget) => (
              <BudgetRow
                key={budget.id}
                budget={budget}
                hidden={hidden}
                onPress={() =>
                  router.push({
                    pathname: '/presupuestos/[id]',
                    params: { id: budget.id, source: AnalyticsSource.Budgets, ...(date ? { date } : {}) },
                  })
                }
              />
            ))}
          </View>

          <View style={styles.footer}>
            <Button label="Nuevo presupuesto" variant="secondary" onPress={() => setFormOpen(true)} />
          </View>
        </>
      )}

      <BudgetFormSheet
        visible={formOpen}
        hidden={hidden}
        onClose={() => setFormOpen(false)}
        onSaved={(detail) =>
          router.push({ pathname: '/presupuestos/[id]', params: { id: detail.budget.id, source: AnalyticsSource.Budgets } })
        }
      />
    </Screen>
  );
}

function SummaryTile({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.tile}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="heading" tabular numberOfLines={1} adjustsFontSizeToFit>
        {value}
      </Typo>
    </View>
  );
}

function lastDayOfMonth(isoDate: string): string {
  const [year, month] = isoDate.split('-').map(Number);
  const last = new Date(year!, month!, 0).getDate();
  return `${isoDate.slice(0, 8)}${String(last).padStart(2, '0')}`;
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  monthRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  todayChip: {
    marginLeft: spacing.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
    borderRadius: radius.pill,
    backgroundColor: colors.accent,
  },
  summary: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    rowGap: spacing.lg,
    marginBottom: spacing.lg,
  },
  tile: {
    width: '50%',
    gap: spacing.xs,
    paddingRight: spacing.sm,
  },
  insight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.lg,
    marginBottom: spacing.lg,
  },
  flex: {
    flex: 1,
  },
  stack: {
    gap: spacing.md,
  },
  footer: {
    marginTop: spacing.xl,
  },
});
