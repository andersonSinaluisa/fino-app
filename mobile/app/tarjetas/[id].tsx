import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { UtilizationBar } from '../../components/creditCards/UtilizationBar';
import { InstallmentPlanRow } from '../../components/creditCards/InstallmentPlanRow';
import { StatementRow } from '../../components/creditCards/StatementRow';
import { TransactionRow } from '../../components/transactions/TransactionRow';
import {
  useArchiveCreditCard,
  useCancelInstallmentPlan,
  useCardPaymentSuggestions,
  useCreditCard,
  useCreditCardStatements,
  useDeleteInstallmentPlan,
  useInstallmentPlans,
  useRestoreCreditCard,
  useTransactions,
} from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { AnalyticsEvent, AnalyticsSource, track } from '../../services/analytics';
import { formatCurrency } from '../../utils/format';
import {
  dueLabel,
  formatCycleRange,
  formatDueDate,
  formatPercent,
  nextPaymentExplanation,
} from '../../utils/creditCards';
import type { CreditCardDetail, InstallmentPlan } from '../../types/api';

type Tab = 'summary' | 'movements' | 'installments' | 'statements';

const TABS: { key: Tab; label: string }[] = [
  { key: 'summary', label: 'Resumen' },
  { key: 'movements', label: 'Movimientos' },
  { key: 'installments', label: 'Cuotas' },
  { key: 'statements', label: 'Estados' },
];

/**
 * Home de una tarjeta. Lo primero que se lee es cuánto se debe, cuánto toca
 * pagar y cuándo, y cuánto cupo queda -- en ese orden y sin mezclarlos: la
 * deuda no es el próximo pago y el cupo no es dinero. Todas las cifras llegan
 * de GET /credit-cards/{id}; aquí no se calcula ninguna.
 */
export default function CreditCardScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string; source?: string; tab?: Tab }>();
  const id = params.id;
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const [tab, setTab] = useState<Tab>(params.tab ?? 'summary');

  const { data, isLoading, isError, refetch, isRefetching } = useCreditCard(id);
  const suggestions = useCardPaymentSuggestions(id, !!data && !data.card.isArchived);
  const archive = useArchiveCreditCard();
  const restore = useRestoreCreditCard();

  const tracked = useRef(false);
  useEffect(() => {
    if (!data || tracked.current) {
      return;
    }
    tracked.current = true;
    track(AnalyticsEvent.CreditCardOpened, {
      source: params.source ?? AnalyticsSource.Unknown,
      needsSetup: data.card.needsSetup,
    });
  }, [data, params.source]);

  if (isLoading || !data) {
    return (
      <Screen>
        <BackButton onPress={() => router.back()} />
        {isError ? (
          <EmptyState icon="alert-circle-outline" title="No encontramos esta tarjeta" actionLabel="Volver" onAction={() => router.back()} />
        ) : (
          <View style={styles.stack}>
            <SkeletonCard />
            <SkeletonCard />
          </View>
        )}
      </Screen>
    );
  }

  const { card } = data;
  const money = (value: number) => formatCurrency(value, { hidden });
  const next = card.nextPayment;

  const confirmArchive = () => {
    Alert.alert(
      `Archivar ${card.name}`,
      'Dejará de aparecer y de reservar dinero. Sus movimientos, estados, cuotas e historial se conservan, y puedes restaurarla cuando quieras.',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Archivar',
          style: 'destructive',
          onPress: () =>
            archive.mutate(card.id, {
              onSuccess: () => router.back(),
              onError: (error) => Alert.alert('No pudimos archivarla', error.message),
            }),
        },
      ],
    );
  };

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <BackButton onPress={() => router.back()} />

      <View style={styles.header}>
        <View style={styles.flex}>
          <Typo variant="title" numberOfLines={1}>
            {card.name}
          </Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {[card.lastFour ? `•••• ${card.lastFour}` : null, card.providerName].filter(Boolean).join('  ·  ')}
          </Typo>
        </View>
        <Pressable
          onPress={() => router.push({ pathname: '/tarjetas/nueva', params: { id: card.id } })}
          accessibilityRole="button"
          accessibilityLabel="Editar tarjeta"
          hitSlop={12}
          style={styles.iconButton}
        >
          <Ionicons name="create-outline" size={20} color={colors.text} />
        </Pressable>
      </View>

      {card.isArchived ? (
        <Card tone="secondary" style={styles.stack}>
          <Typo variant="body">Esta tarjeta está archivada: no aparece en Cuentas ni reserva dinero.</Typo>
          <Button
            label="Restaurar tarjeta"
            variant="secondary"
            loading={restore.isPending}
            onPress={() => restore.mutate(card.id, { onError: (error) => Alert.alert('No pudimos restaurarla', error.message) })}
          />
        </Card>
      ) : null}

      {card.needsSetup ? (
        <Card tone="secondary" style={styles.stackSection}>
          <Typo variant="subheading">Completa los datos de tu tarjeta</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Con el cupo, el día de corte y el día de pago Fino puede decirte cuánto toca pagar, cuándo, y reservarlo en tu Disponible.
          </Typo>
          <Button label="Completar" onPress={() => router.push({ pathname: '/tarjetas/nueva', params: { id: card.id } })} />
        </Card>
      ) : null}

      <View style={styles.hero}>
        <Typo variant="overline" color={colors.textSecondary}>
          DEUDA ACTUAL
        </Typo>
        <Typo variant="display" tabular>
          {money(card.currentDebt)}
        </Typo>
        {card.creditBalance > 0 ? (
          <Typo variant="caption" color={colors.success}>
            Saldo a favor {money(card.creditBalance)}
          </Typo>
        ) : null}
        {card.deferredDebt > 0 ? (
          <Typo variant="caption" color={colors.textSecondary}>
            Incluye {money(card.deferredDebt)} de compras en cuotas que se cobran en próximos estados.
          </Typo>
        ) : null}
        {card.balanceType === 'Estimated' ? (
          <Typo variant="caption" color={colors.textSecondary}>
            Estimada con tus movimientos. Compárala con tu banco cuando puedas.
          </Typo>
        ) : null}
      </View>

      {next ? (
        <Card style={styles.stack}>
          <View style={styles.rowBetween}>
            <Typo variant="overline" color={next.isOverdue ? colors.danger : colors.textSecondary}>
              PRÓXIMO PAGO
            </Typo>
            <Badge label={dueLabel(next)} tone={next.isOverdue ? 'danger' : next.daysUntilDue <= 5 ? 'attention' : 'neutral'} />
          </View>
          <View style={styles.rowBetween}>
            <Typo variant="title" tabular>
              {money(next.amount)}
            </Typo>
            <Typo variant="heading" color={next.isOverdue ? colors.danger : colors.text}>
              {formatDueDate(next.dueDate)}
            </Typo>
          </View>
          {next.minimumPayment !== null ? (
            <Typo variant="body" color={colors.textSecondary}>
              Pago mínimo {money(next.minimumPayment)}
            </Typo>
          ) : null}
          <Typo variant="caption" color={colors.textSecondary}>
            {nextPaymentExplanation(next)}
          </Typo>
          <Typo variant="caption" color={card.autoReserve ? colors.text : colors.textSecondary}>
            {card.autoReserve
              ? `${money(card.committedContribution)} reservados en tu Comprometido.`
              : 'No se reserva en tu Disponible. Puedes activarlo editando la tarjeta.'}
          </Typo>
        </Card>
      ) : !card.needsSetup ? (
        <Card style={styles.stack}>
          <Typo variant="overline" color={colors.textSecondary}>
            PRÓXIMO PAGO
          </Typo>
          <Typo variant="body">No tienes pagos pendientes en esta tarjeta.</Typo>
        </Card>
      ) : null}

      {!card.needsSetup && card.creditLimit !== null ? (
        <Card style={styles.stackSection}>
          <View style={styles.rowBetween}>
            <Typo variant="overline" color={colors.textSecondary}>
              UTILIZACIÓN
            </Typo>
            <Typo variant="bodyStrong" tabular color={card.isOverLimit ? colors.danger : colors.text}>
              {formatPercent(card.utilizationPercent)}
            </Typo>
          </View>
          <UtilizationBar utilizationPercent={card.utilizationPercent} isOverLimit={card.isOverLimit} />
          <Typo variant="body" color={colors.textSecondary} tabular>
            {money(card.currentDebt)} de {money(card.creditLimit)}
          </Typo>
          <View style={styles.rowBetween}>
            <Typo variant="body">Cupo disponible</Typo>
            <Typo variant="bodyStrong" tabular>
              {card.availableCredit === null ? '—' : money(card.availableCredit)}
            </Typo>
          </View>
          <Typo variant="caption" color={colors.textSecondary}>
            El cupo es crédito, no dinero: no se suma a Tu dinero ni a tu Disponible.
          </Typo>
        </Card>
      ) : null}

      {!card.isArchived ? (
        <View style={[styles.actions, styles.section]}>
          <View style={styles.flex}>
            <Button label="Pagar" variant="accent" onPress={() => router.push({ pathname: '/tarjetas/pagar', params: { id: card.id } })} />
          </View>
          <View style={styles.flex}>
            <Button label="Registrar" variant="secondary" onPress={() => router.push({ pathname: '/tarjetas/movimiento', params: { id: card.id } })} />
          </View>
          <View style={styles.flex}>
            <Button label="Importar" variant="secondary" onPress={() => router.push(`/cuentas/importar?accountId=${card.id}`)} />
          </View>
        </View>
      ) : null}

      {suggestions.data && suggestions.data.length > 0 ? (
        <Pressable
          onPress={() => router.push({ pathname: '/tarjetas/pagar', params: { id: card.id } })}
          accessibilityRole="button"
          style={styles.suggestion}
        >
          <Ionicons name="git-compare-outline" size={18} color={colors.text} />
          <Typo variant="body" style={styles.flex}>
            {suggestions.data.length === 1
              ? 'Encontramos un débito en tu banco que parece un pago a esta tarjeta. Revísalo.'
              : `Encontramos ${suggestions.data.length} débitos en tu banco que parecen pagos a esta tarjeta. Revísalos.`}
          </Typo>
          <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
        </Pressable>
      ) : null}

      <View style={[styles.tabs, styles.section]} accessibilityRole="tablist">
        {TABS.map((item) => (
          <Pressable
            key={item.key}
            onPress={() => setTab(item.key)}
            accessibilityRole="tab"
            accessibilityState={{ selected: tab === item.key }}
            style={[styles.tab, tab === item.key ? styles.tabSelected : null]}
          >
            <Typo variant="caption" color={tab === item.key ? colors.onPrimary : colors.textSecondary}>
              {item.label}
            </Typo>
          </Pressable>
        ))}
      </View>

      {tab === 'summary' ? <SummaryTab data={data} hidden={hidden} onSeeInstallments={() => setTab('installments')} onSeeMovements={() => setTab('movements')} /> : null}
      {tab === 'movements' ? <MovementsTab cardId={card.id} hidden={hidden} /> : null}
      {tab === 'installments' ? <InstallmentsTab cardId={card.id} hidden={hidden} /> : null}
      {tab === 'statements' ? <StatementsTab cardId={card.id} hidden={hidden} canDeclare={!card.needsSetup} /> : null}

      {!card.isArchived ? (
        <View style={styles.section}>
          <Button label="Archivar tarjeta" variant="ghost" onPress={confirmArchive} loading={archive.isPending} />
        </View>
      ) : null}
    </Screen>
  );
}

function SummaryTab({
  data,
  hidden,
  onSeeInstallments,
  onSeeMovements,
}: {
  data: CreditCardDetail;
  hidden: boolean;
  onSeeInstallments: () => void;
  onSeeMovements: () => void;
}) {
  const router = useRouter();
  const money = (value: number) => formatCurrency(value, { hidden });
  const month = data.thisMonth;
  const lines: { label: string; value: number; always?: boolean }[] = [
    { label: 'Compras', value: month.purchases, always: true },
    { label: 'Pagos', value: month.payments, always: true },
    { label: 'Intereses', value: month.interest, always: true },
    { label: 'Comisiones', value: month.fees },
    { label: 'Devoluciones', value: month.refunds },
    { label: 'Avances en efectivo', value: month.cashAdvances },
  ];

  return (
    <View style={styles.tabContent}>
      {data.currentCycle ? (
        <Typo variant="caption" color={colors.textSecondary}>
          Ciclo actual: {formatCycleRange(data.currentCycle.start, data.currentCycle.closing)} · corte {formatDueDate(data.currentCycle.closing)}
        </Typo>
      ) : null}

      <View>
        <Typo variant="overline" color={colors.textSecondary} style={styles.sectionLabel}>
          ESTE MES · {month.label.toUpperCase()}
        </Typo>
        <View style={styles.listCard}>
          {lines
            .filter((line) => line.always || line.value !== 0)
            .map((line, index) => (
              <View key={line.label}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <View style={[styles.rowBetween, styles.listRow]}>
                  <Typo variant="body">{line.label}</Typo>
                  <Typo variant="bodyStrong" tabular>
                    {money(line.value)}
                  </Typo>
                </View>
              </View>
            ))}
        </View>
      </View>

      {data.lastStatement ? (
        <View>
          <Typo variant="overline" color={colors.textSecondary} style={styles.sectionLabel}>
            ÚLTIMO ESTADO
          </Typo>
          <View style={styles.listCard}>
            <StatementRow statement={data.lastStatement} hidden={hidden} />
          </View>
        </View>
      ) : null}

      {data.activeInstallments.length > 0 ? (
        <View>
          <Typo variant="overline" color={colors.textSecondary} style={styles.sectionLabel}>
            PRÓXIMAS CUOTAS
          </Typo>
          <View style={styles.listCard}>
            {data.activeInstallments.slice(0, 3).map((plan, index) => (
              <View key={plan.id}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <InstallmentPlanRow plan={plan} hidden={hidden} onPress={onSeeInstallments} />
              </View>
            ))}
          </View>
        </View>
      ) : null}

      <View>
        <Typo variant="overline" color={colors.textSecondary} style={styles.sectionLabel}>
          ÚLTIMOS MOVIMIENTOS
        </Typo>
        {data.recentMovements.length > 0 ? (
          <View style={styles.listCard}>
            {data.recentMovements.map((transaction, index) => (
              <View key={transaction.id}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <TransactionRow transaction={transaction} hidden={hidden} onPress={() => router.push(`/movimiento/${transaction.id}`)} />
              </View>
            ))}
            <Pressable onPress={onSeeMovements} accessibilityRole="button" style={styles.seeAll}>
              <Typo variant="caption">Ver todos los movimientos</Typo>
            </Pressable>
          </View>
        ) : (
          <Typo variant="body" color={colors.textSecondary}>
            Todavía no hay movimientos. Regístralos a mano o importa el estado de cuenta de la tarjeta.
          </Typo>
        )}
      </View>
    </View>
  );
}

function MovementsTab({ cardId, hidden }: { cardId: string; hidden: boolean }) {
  const router = useRouter();
  const query = useTransactions({ accountId: cardId });
  const items = query.data?.pages.flatMap((page) => page.items) ?? [];

  if (query.isLoading) {
    return <SkeletonCard />;
  }

  if (items.length === 0) {
    return (
      <Typo variant="body" color={colors.textSecondary} style={styles.tabContent}>
        Todavía no hay movimientos en esta tarjeta.
      </Typo>
    );
  }

  return (
    <View style={[styles.listCard, styles.tabContent]}>
      {items.map((transaction, index) => (
        <View key={transaction.id}>
          {index > 0 ? <View style={styles.divider} /> : null}
          <TransactionRow transaction={transaction} hidden={hidden} onPress={() => router.push(`/movimiento/${transaction.id}`)} />
        </View>
      ))}
      {query.hasNextPage ? (
        <Pressable onPress={() => void query.fetchNextPage()} accessibilityRole="button" style={styles.seeAll}>
          {query.isFetchingNextPage ? <ActivityIndicator color={colors.text} /> : <Typo variant="caption">Cargar más</Typo>}
        </Pressable>
      ) : null}
    </View>
  );
}

function InstallmentsTab({ cardId, hidden }: { cardId: string; hidden: boolean }) {
  const plans = useInstallmentPlans(cardId);
  const cancel = useCancelInstallmentPlan();
  const remove = useDeleteInstallmentPlan();

  const manage = (plan: InstallmentPlan) => {
    if (plan.status !== 'Active') {
      return;
    }

    Alert.alert(plan.description, 'La compra sigue siendo un solo gasto. Esto solo cambia cuándo se paga la deuda.', [
      { text: 'Cerrar', style: 'cancel' },
      {
        text: 'Precancelar',
        onPress: () =>
          cancel.mutate({ id: cardId, planId: plan.id }, { onError: (error) => Alert.alert('No pudimos precancelarlo', error.message) }),
      },
      {
        text: 'No era diferida',
        style: 'destructive',
        onPress: () =>
          remove.mutate({ id: cardId, planId: plan.id }, { onError: (error) => Alert.alert('No pudimos quitarlo', error.message) }),
      },
    ]);
  };

  if (plans.isLoading) {
    return <SkeletonCard />;
  }

  const list = plans.data ?? [];
  return (
    <View style={styles.tabContent}>
      <Typo variant="caption" color={colors.textSecondary}>
        Para diferir una compra, ábrela desde Movimientos y toca «Diferir en cuotas». Solo la cuota del próximo estado se reserva; el resto es deuda futura.
      </Typo>
      {list.length === 0 ? (
        <Typo variant="body" color={colors.textSecondary}>
          No tienes compras a cuotas en esta tarjeta.
        </Typo>
      ) : (
        <View style={styles.listCard}>
          {list.map((plan, index) => (
            <View key={plan.id}>
              {index > 0 ? <View style={styles.divider} /> : null}
              <InstallmentPlanRow plan={plan} hidden={hidden} onPress={plan.status === 'Active' ? manage : undefined} />
            </View>
          ))}
        </View>
      )}
    </View>
  );
}

function StatementsTab({ cardId, hidden, canDeclare }: { cardId: string; hidden: boolean; canDeclare: boolean }) {
  const router = useRouter();
  const statements = useCreditCardStatements(cardId);

  if (statements.isLoading) {
    return <SkeletonCard />;
  }

  const list = statements.data ?? [];
  return (
    <View style={styles.tabContent}>
      {canDeclare ? (
        <Button
          label="Registrar estado de cuenta"
          variant="secondary"
          onPress={() => router.push({ pathname: '/tarjetas/estado', params: { id: cardId } })}
        />
      ) : null}
      <Typo variant="caption" color={colors.textSecondary}>
        Sin las cifras de tu banco, Fino calcula cada estado con tus movimientos. Regístralas (o importa el estado) para usar el total y el mínimo oficiales.
      </Typo>
      {list.length === 0 ? (
        <Typo variant="body" color={colors.textSecondary}>
          Aún no hay estados.
        </Typo>
      ) : (
        <View style={styles.listCard}>
          {list.map((statement, index) => (
            <View key={statement.closingDate}>
              {index > 0 ? <View style={styles.divider} /> : null}
              <StatementRow statement={statement} hidden={hidden} />
            </View>
          ))}
        </View>
      )}
    </View>
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

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    marginBottom: spacing.lg,
  },
  iconButton: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  hero: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  stack: {
    gap: spacing.sm,
  },
  section: {
    marginTop: spacing.lg,
  },
  stackSection: {
    gap: spacing.sm,
    marginTop: spacing.lg,
  },
  rowBetween: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  actions: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  suggestion: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: 'rgba(199, 243, 107, 0.28)',
    borderRadius: radius.md,
    padding: spacing.md,
    marginTop: spacing.lg,
  },
  tabs: {
    flexDirection: 'row',
    backgroundColor: colors.surface,
    borderRadius: radius.pill,
    padding: 4,
    gap: 4,
  },
  tab: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: spacing.sm,
    borderRadius: radius.pill,
  },
  tabSelected: {
    backgroundColor: colors.primary,
  },
  tabContent: {
    marginTop: spacing.lg,
    gap: spacing.lg,
  },
  sectionLabel: {
    marginBottom: spacing.sm,
  },
  listCard: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
  },
  listRow: {
    paddingVertical: spacing.md,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  seeAll: {
    alignItems: 'center',
    paddingVertical: spacing.md,
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  flex: {
    flex: 1,
  },
});
