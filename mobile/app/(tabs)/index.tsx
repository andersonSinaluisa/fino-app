import { ScrollView, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Screen, SectionHeader, SkeletonCard, Skeleton, EmptyState, Typo, Button } from '../../components/ui';
import { BalanceHeader } from '../../components/home/BalanceHeader';
import { MonthTiles } from '../../components/home/MonthTiles';
import { InsightCard } from '../../components/home/InsightCard';
import { CategoryBreakdown } from '../../components/home/CategoryBreakdown';
import { AccountCard } from '../../components/accounts/AccountCard';
import { TransactionRow } from '../../components/transactions/TransactionRow';
import { useSummary } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';

export default function HomeScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useSummary();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const toggleAmounts = usePreferencesStore((state) => state.toggleAmounts);

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

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
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
          title="Agrega tu primera cuenta"
          body="Conecta un banco o una billetera para empezar a ver todo tu dinero en un solo lugar."
          actionLabel="Agregar cuenta"
          onAction={() => router.push('/cuentas/agregar')}
        />
      ) : (
        <>
          <MonthTiles income={data.month.income} expense={data.month.expense} hidden={hidden} />

          {data.insights.length > 0 ? (
            <View style={styles.section}>
              <SectionHeader title="Para ti" />
              <ScrollView
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.insightRow}
              >
                {data.insights.map((insight) => (
                  <InsightCard key={insight.code} insight={insight} />
                ))}
              </ScrollView>
            </View>
          ) : null}

          <View style={styles.section}>
            <SectionHeader title="Tus cuentas" actionLabel="Ver todas" onAction={() => router.push('/(tabs)/cuentas')} />
            <View style={styles.stack}>
              {data.accounts.slice(0, 4).map((account) => (
                <AccountCard
                  key={account.id}
                  account={account}
                  hidden={hidden}
                  compact
                  onPress={() => router.push('/(tabs)/cuentas')}
                />
              ))}
            </View>
          </View>

          {data.categoryBreakdown.length > 0 ? (
            <View style={styles.section}>
              <SectionHeader title="En qué gastas" />
              <CategoryBreakdown items={data.categoryBreakdown} hidden={hidden} />
            </View>
          ) : null}

          <View style={styles.section}>
            <SectionHeader
              title="Movimientos recientes"
              actionLabel="Ver todos"
              onAction={() => router.push('/(tabs)/movimientos')}
            />

            {data.recentTransactions.length === 0 ? (
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
                {data.recentTransactions.map((transaction, index) => (
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
  stack: {
    gap: spacing.md,
  },
  insightRow: {
    gap: spacing.md,
    paddingRight: spacing.lg,
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
