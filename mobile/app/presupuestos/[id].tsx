import { useEffect, useRef, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { BudgetProgressBar } from '../../components/budgets/BudgetProgressBar';
import { BudgetFormSheet } from '../../components/budgets/BudgetFormSheet';
import { TransactionRow } from '../../components/transactions/TransactionRow';
import { useBudget, useBudgetMovements, useDeleteBudget, useUpdateBudget } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { AnalyticsEvent, AnalyticsSource, track } from '../../services/analytics';
import { formatCurrency } from '../../utils/format';
import { LEVEL_PRESENTATION, PERIOD_LABELS, PRIORITY_LABELS, budgetStatusMessage } from '../../utils/budgets';

/**
 * Detalle de un presupuesto: cuánto, cuánto se gastó, qué queda, por qué
 * (los movimientos exactos que sumaron) y, si reserva dinero, cómo afecta a
 * Disponible. `date` (opcional) abre otro período del historial.
 */
export default function BudgetDetailScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string; date?: string; source?: string }>();
  const id = params.id;
  const date = params.date || undefined;
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const [editing, setEditing] = useState(false);

  const { data, isLoading, isError, refetch, isRefetching } = useBudget(id, date);
  const movements = useBudgetMovements(id, date);
  const updateBudget = useUpdateBudget();
  const deleteBudget = useDeleteBudget();

  const tracked = useRef(false);
  useEffect(() => {
    if (!data || tracked.current) {
      return;
    }
    tracked.current = true;
    track(AnalyticsEvent.BudgetOpened, {
      source: params.source ?? AnalyticsSource.Unknown,
      level: data.budget.progress?.level,
    });
  }, [data, params.source]);

  const money = (value: number) => formatCurrency(value, { hidden });

  if (isLoading || !data) {
    return (
      <Screen>
        <BackButton onPress={() => router.back()} />
        {isError ? (
          <EmptyState icon="alert-circle-outline" title="No encontramos este presupuesto" actionLabel="Volver" onAction={() => router.back()} />
        ) : (
          <View style={styles.stack}>
            <SkeletonCard />
            <SkeletonCard />
          </View>
        )}
      </Screen>
    );
  }

  const { budget, insights, history } = data;
  const progress = budget.progress;
  const level = progress ? LEVEL_PRESENTATION[progress.level] : null;

  const togglePause = () => {
    updateBudget.mutate(
      {
        id: budget.id,
        amount: budget.amount,
        categoryId: budget.categoryId,
        name: budget.name,
        endDate: budget.endDate,
        reserveFunds: budget.reserveFunds,
        priority: budget.priority,
        isActive: !budget.isActive,
      },
      {
        onSuccess: () => track(AnalyticsEvent.BudgetUpdated, { reserves: budget.reserveFunds, paused: budget.isActive }),
        onError: (error) => Alert.alert('No pudimos cambiar el presupuesto', error.message),
      },
    );
  };

  const confirmDelete = () => {
    Alert.alert(
      'Eliminar presupuesto',
      `"${budget.name}" dejará de existir. Tus movimientos no cambian.`,
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Eliminar',
          style: 'destructive',
          onPress: () =>
            deleteBudget.mutate(budget.id, {
              onSuccess: () => {
                track(AnalyticsEvent.BudgetDeleted);
                router.back();
              },
              onError: (error) => Alert.alert('No pudimos eliminarlo', error.message),
            }),
        },
      ],
    );
  };

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <BackButton onPress={() => router.back()} />

      <View style={styles.header}>
        <Typo variant="title">{budget.name}</Typo>
        <Typo variant="subheading" color={colors.textSecondary}>
          {budget.window?.label ?? PERIOD_LABELS[budget.period]}
        </Typo>
        <View style={styles.badges}>
          {level ? <Badge label={level.label} tone={level.tone} /> : null}
          {budget.reserveFunds ? <Badge label="Reservado" tone="accent" /> : null}
          {!budget.isActive ? <Badge label="Pausado" /> : null}
          <Badge label={`${PERIOD_LABELS[budget.period]} · ${PRIORITY_LABELS[budget.priority]}`} />
        </View>
      </View>

      {progress ? (
        <Card style={styles.stack}>
          <Row label="Presupuesto" value={money(progress.amount)} />
          <Row label="Gastado" value={money(progress.spent)} />
          <Row label={progress.overspent > 0 ? 'Excedido' : 'Restante'} value={money(progress.overspent > 0 ? progress.overspent : progress.remaining)} strong />
          <BudgetProgressBar progress={progress} muted={!budget.isActive} height={10} />
          <View style={styles.rowBetween}>
            <Typo variant="caption" color={progress.level === 'Exceeded' ? colors.danger : colors.textSecondary}>
              {budgetStatusMessage(progress, hidden)}
            </Typo>
            <Typo variant="caption" tabular>
              {Math.floor(progress.percentUsed)}% utilizado
            </Typo>
          </View>
          {progress.isCurrentWindow && progress.dailyAllowance !== null && progress.daysRemaining > 1 ? (
            <Typo variant="caption" color={colors.textSecondary}>
              Quedan {progress.daysRemaining} días · {money(progress.dailyAllowance)} por día
            </Typo>
          ) : null}
        </Card>
      ) : null}

      {budget.reserveFunds ? (
        <Card tone="secondary" style={styles.reserveCard}>
          <View style={styles.inline}>
            <Ionicons name="lock-closed-outline" size={18} color={colors.text} />
            <Typo variant="subheading">Dinero reservado</Typo>
          </View>
          {!budget.isActive ? (
            <Typo variant="body" color={colors.textSecondary}>
              Está pausado: mientras tanto no reserva nada y tu Disponible no lo descuenta.
            </Typo>
          ) : progress && progress.isCurrentWindow ? (
            <Typo variant="body" color={colors.textSecondary}>
              {progress.reserved > 0
                ? `${money(progress.reserved)} que aún no gastas ya tienen destino: se restan de tu Disponible hasta que los gastes o termine el período.`
                : 'Ya gastaste todo lo reservado, así que no está restando nada de tu Disponible.'}
            </Typo>
          ) : (
            <Typo variant="body" color={colors.textSecondary}>
              Solo el período actual reserva dinero. Este período no afecta a tu Disponible de hoy.
            </Typo>
          )}
        </Card>
      ) : null}

      {insights.length > 0 ? (
        <View style={[styles.stack, styles.section]}>
          {insights.map((insight) => (
            <View key={insight.kind} style={styles.insight}>
              <Ionicons name="bulb-outline" size={16} color={colors.text} />
              <Typo variant="body" style={styles.flex}>
                {hidden ? insight.messageWithoutAmounts : insight.message}
              </Typo>
            </View>
          ))}
        </View>
      ) : null}

      <View style={styles.section}>
        <Typo variant="heading" style={styles.sectionTitle}>
          Movimientos de este presupuesto
        </Typo>
        {movements.isLoading ? (
          <SkeletonCard />
        ) : movements.data && movements.data.items.length > 0 ? (
          <View style={styles.card}>
            {movements.data.items.map((transaction, index) => (
              <View key={transaction.id}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <TransactionRow
                  transaction={transaction}
                  hidden={hidden}
                  onPress={() => router.push(`/movimiento/${transaction.id}`)}
                />
              </View>
            ))}
          </View>
        ) : (
          <Typo variant="body" color={colors.textSecondary}>
            Todavía no hay movimientos en este período.
          </Typo>
        )}
      </View>

      {history.length > 1 ? (
        <View style={styles.section}>
          <Typo variant="heading" style={styles.sectionTitle}>
            Períodos
          </Typo>
          <View style={styles.card}>
            {history.map((item, index) => (
              <View key={item.window.start}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <Pressable
                  onPress={() => router.setParams({ date: item.window.start })}
                  accessibilityRole="button"
                  accessibilityLabel={`${item.window.label}: ${Math.floor(item.percentUsed)}% usado`}
                  style={[styles.historyRow, styles.rowBetween]}
                >
                  <Typo variant="body" color={item.window.start === budget.window?.start ? colors.text : colors.textSecondary}>
                    {item.window.label}
                  </Typo>
                  <Typo variant="caption" tabular>
                    {hidden ? `${Math.floor(item.percentUsed)}%` : `${money(item.spent)} de ${money(item.amount)}`}
                  </Typo>
                </Pressable>
              </View>
            ))}
          </View>
        </View>
      ) : null}

      <View style={[styles.actions, styles.section]}>
        <Button label="Editar presupuesto" variant="secondary" onPress={() => setEditing(true)} />
        <Button
          label={budget.isActive ? 'Pausar' : 'Reanudar'}
          variant="ghost"
          onPress={togglePause}
          loading={updateBudget.isPending}
        />
        <Button label="Eliminar" variant="danger" onPress={confirmDelete} loading={deleteBudget.isPending} />
      </View>

      <BudgetFormSheet visible={editing} budget={budget} hidden={hidden} onClose={() => setEditing(false)} />
    </Screen>
  );
}

function BackButton({ onPress }: { onPress: () => void }) {
  return (
    <Pressable onPress={onPress} hitSlop={12} style={styles.back} accessibilityRole="button" accessibilityLabel="Volver">
      <Ionicons name="chevron-back" size={20} color={colors.text} />
      <Typo variant="caption" color={colors.textSecondary}>
        Volver
      </Typo>
    </Pressable>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.rowBetween}>
      <Typo variant="body" color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'heading' : 'bodyStrong'} tabular>
        {value}
      </Typo>
    </View>
  );
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
  badges: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
    marginTop: spacing.sm,
  },
  stack: {
    gap: spacing.md,
  },
  reserveCard: {
    gap: spacing.md,
    marginTop: spacing.xl,
  },
  section: {
    marginTop: spacing.xl,
  },
  sectionTitle: {
    marginBottom: spacing.md,
  },
  rowBetween: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.md,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  insight: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  flex: {
    flex: 1,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.xs,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  historyRow: {
    paddingVertical: spacing.md,
  },
  actions: {
    gap: spacing.sm,
  },
});
