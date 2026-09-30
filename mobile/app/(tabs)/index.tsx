import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Screen, SkeletonCard, Skeleton, EmptyState, Typo, Button } from '../../components/ui';
import { BalanceHeader } from '../../components/home/BalanceHeader';
import { AvailableMoneySection } from '../../components/home/AvailableMoneySection';
import { CategoryDonutCard } from '../../components/home/CategoryDonutCard';
import { MonthSummaryCard } from '../../components/home/MonthSummaryCard';
import { MonthChart } from '../../components/home/MonthChart';
import { PulseCard } from '../../components/home/PulseCard';
import { AccountsSummaryList } from '../../components/home/AccountsSummaryList';
import { PendingSyncBanner } from '../../components/quick-entry/PendingSyncBanner';
import { TransactionRow } from '../../components/transactions/TransactionRow';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { useSummary, useAnalyticsDashboard, usePulses, useCommittedMoney, useBudgets } from '../../hooks/queries';
import { BudgetHomeCard } from '../../components/home/BudgetHomeCard';
import { usePreferencesStore } from '../../store/preferencesStore';
import { useOnboardingStore } from '../../store/onboardingStore';
import { useAuthStore } from '../../store/authStore';
import { pickNextPayment } from '../../utils/recurringPayments';
import { FirstAccountReadyCard } from '../../components/home/FirstAccountReadyCard';
import { AnalyticsEvent, AnalyticsSource, ValueSource, track } from '../../services/analytics';
import { trackFirstValueIfReached } from '../../services/analytics/firstValue';

const MAX_RECENT_TRANSACTIONS = 3;

/**
 * Rediseño de Home (2026-09): la pantalla ya no intenta ser un dashboard con
 * todo -- responde 4 preguntas (¿cuánto tengo?, ¿cuánto puedo gastar?, ¿cómo
 * voy este mes?, ¿algo requiere mi atención?) y deja el resto a Movimientos,
 * Estadísticas y Cuentas. Ningún componente de negocio se duplicó: cada
 * sección reutiliza los mismos hooks/datos/lógica que ya usaban las pantallas
 * completas (useSummary, useAnalyticsDashboard, useCommittedMoney, useBudgets,
 * pickNextPayment, accountBalanceStatus, etc.).
 */
export default function HomeScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useSummary();
  const { data: dashboard } = useAnalyticsDashboard({ period: 'month' });
  const { data: pulses } = usePulses();
  // Presupuestos + Comprometido: calculados en el backend, nunca aquí.
  const { data: committed } = useCommittedMoney();
  const { data: budgetOverview } = useBudgets();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const toggleAmounts = usePreferencesStore((state) => state.toggleAmounts);
  const onboarding = useAuthStore((state) => state.user?.onboarding);
  const firstAccountCardDismissed = useOnboardingStore((state) => state.firstAccountCardDismissed);
  const dismissFirstAccountCard = useOnboardingStore((state) => state.dismissFirstAccountCard);

  /**
   * §11: el momento de valor se decide AQUÍ y no al terminar la importación.
   *
   * Importar un archivo no es el valor; ver tu dinero ordenado sí. Por eso la
   * condición es "estoy en Inicio y el resumen trajo datos de verdad", que es
   * literalmente lo que el usuario acaba de ver en pantalla. `trackOnce` se
   * encarga de que sea una sola vez por usuario aunque vuelva a Inicio mil
   * veces.
   */
  const hasUsefulData = Boolean(data && data.accounts.length > 0 && data.recentTransactions.length > 0);
  const cameFromImport = Boolean(onboarding?.hasImportedData);

  useEffect(() => {
    track(AnalyticsEvent.HomeViewed, { hasData: hasUsefulData });

    void trackFirstValueIfReached(
      hasUsefulData,
      cameFromImport ? ValueSource.StatementImport : ValueSource.CashManual,
    );
  }, [hasUsefulData, cameFromImport]);

  if (isLoading || !data) {
    return (
      <Screen>
        <Skeleton height={18} width="45%" />
        <View style={styles.loadingGap} />
        <Skeleton height={46} width="70%" />
        <View style={styles.loadingGap} />
        <SkeletonCard />
        <View style={styles.loadingGap} />
        <SkeletonCard />
      </Screen>
    );
  }

  const hasAccounts = data.accounts.length > 0;
  // Onboarding funcional: la tarjeta "Tu primera cuenta está lista" solo
  // cuando la primera importación real vino del onboarding (no de alguien
  // que agregó una cuenta manual sin más) -- y solo hasta que se descarta.
  const showFirstAccountCard = Boolean(onboarding?.firstImportCompletedAt) && !firstAccountCardDismissed && hasAccounts;
  const nextPayment = pickNextPayment(dashboard?.recurringPayments ?? null);
  const recentTransactions = data.recentTransactions.slice(0, MAX_RECENT_TRANSACTIONS);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <PendingSyncBanner />

      <BalanceHeader
        greeting={data.greeting}
        name={data.displayName}
        total={data.totalBalance}
        accountCount={data.accountCount}
        estimated={data.anyEstimatedBalance}
        hidden={hidden}
        onToggleHidden={toggleAmounts}
      />

      {!hasAccounts ? (
        <EmptyState
          icon="wallet-outline"
          title="Vamos a poner Fino a funcionar"
          body="Conecta un banco para ver tus movimientos reales -- no números en cero."
          actionLabel="Conectar mi banco"
          onAction={() => router.push('/cuentas/agregar')}
        />
      ) : (
        <>
          {showFirstAccountCard ? (
            <FirstAccountReadyCard
              account={data.accounts[0]}
              accountCount={data.accountCount}
              onAddAnother={() => router.push('/cuentas/agregar')}
              onDismiss={dismissFirstAccountCard}
            />
          ) : null}
          {data.categoryBreakdown.length > 0 ? (
            <View style={styles.topSection}>
              <CategoryDonutCard
                items={data.categoryBreakdown}
                totalExpense={data.month.expense}
                hidden={hidden}
              />
            </View>
          ) : null}

          <AvailableMoneySection
            summary={data}
            committed={committed}
            hidden={hidden}
            onOpenCommitted={() => router.push({ pathname: '/comprometido', params: { source: AnalyticsSource.Home } })}
          />

          <View style={styles.budgetSection}>
            <BudgetHomeCard
              overview={budgetOverview}
              hidden={hidden}
              onOpenBudgets={() => router.push('/presupuestos')}
              onOpenBudget={(id) =>
                router.push({ pathname: '/presupuestos/[id]', params: { id, source: AnalyticsSource.Home } })
              }
            />
          </View>

          <View style={styles.section}>
            <MonthSummaryCard
              income={data.month.income}
              expense={data.month.expense}
              net={data.month.net}
              monthComparison={data.monthComparison}
              hidden={hidden}
            />
          </View>

          {dashboard && dashboard.balanceEvolution.length > 0 ? (
            <View style={styles.section}>
              <MonthChart
                points={dashboard.balanceEvolution}
                monthExpense={data.month.expense}
                monthComparison={data.monthComparison}
                hidden={hidden}
              />
            </View>
          ) : null}

          {(pulses && pulses.length > 0) || nextPayment ? (
            <View style={styles.section}>
              {pulses && pulses.length > 0 ? (
                <SectionHeader title="Actividad de FINO" actionLabel="Ver todo" onAction={() => router.push('/pulso')} />
              ) : null}
              <PulseCard pulses={pulses} nextPayment={nextPayment} hidden={hidden} />
            </View>
          ) : null}

          <View style={styles.section}>
            <AccountsSummaryList
              accounts={data.accounts}
              hidden={hidden}
              onSeeAll={() => router.push('/(tabs)/cuentas')}
              onPressAccount={() => router.push('/(tabs)/cuentas')}
            />
          </View>

          <View style={styles.section}>
            <SectionHeader
              title="Movimientos recientes"
              actionLabel="Ver todos"
              onAction={() => router.push('/(tabs)/movimientos')}
            />

            {recentTransactions.length === 0 ? (
              <View style={styles.emptyCard}>
                <Typo variant="body" color={colors.textSecondary}>
                  Todavía no hay movimientos. Importa un estado de cuenta para empezar.
                </Typo>
                <View style={styles.emptyAction}>
                  <Button
                    label="Importar movimientos"
                    variant="secondary"
                    compact
                    fullWidth={false}
                    onPress={() => router.push('/(tabs)/cuentas')}
                  />
                </View>
              </View>
            ) : (
              <View style={styles.card}>
                {recentTransactions.map((transaction, index) => (
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
            )}
          </View>
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  loadingGap: {
    height: spacing.lg,
  },
  section: {
    marginTop: spacing.xxl,
  },
  budgetSection: {
    marginBottom: spacing.md,
  },
  topSection: {
    marginTop: spacing.xxl,
    marginBottom: spacing.xxl,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: 22,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.xs,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  emptyCard: {
    backgroundColor: colors.surface,
    borderRadius: 22,
    padding: spacing.lg,
    gap: spacing.md,
  },
  emptyAction: {
    alignItems: 'flex-start',
  },
});
