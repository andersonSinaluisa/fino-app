import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { AccountCard } from '../../components/accounts/AccountCard';
import { CreditCardTile } from '../../components/creditCards/CreditCardTile';
import { useAccounts, useCreditCards, useDeleteFinancialAccount } from '../../hooks/queries';
import { AnalyticsSource } from '../../services/analytics';
import { usePreferencesStore } from '../../store/preferencesStore';
import { formatCurrency } from '../../utils/format';

export default function AccountsScreen() {
  const router = useRouter();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data, isLoading, refetch, isRefetching } = useAccounts();
  const cards = useCreditCards();
  const deleteAccount = useDeleteFinancialAccount();

  const confirmDelete = (id: string, alias: string) => {
    Alert.alert(
      `Eliminar ${alias}`,
      'Se eliminarán la cuenta, su historial de movimientos y sus importaciones. Esta acción no se puede deshacer.',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Eliminar',
          style: 'destructive',
          onPress: () =>
            deleteAccount.mutate(id, {
              onSuccess: (result) =>
                Alert.alert('Cuenta eliminada', `Se borraron ${result.transactionsDeleted} movimiento(s).`),
              onError: () => Alert.alert('No pudimos eliminarla', 'Inténtalo de nuevo más tarde.'),
            }),
        },
      ],
    );
  };

  // Tarjetas de crédito: "Mi dinero" son solo las cuentas que NO son deuda
  // (`isLiability` lo decide el backend). Una tarjeta y su cupo nunca suman aquí.
  const moneyAccounts = (data ?? []).filter((account) => !account.isLiability);
  const total = moneyAccounts.reduce((sum, account) => sum + account.balance, 0);
  const anyEstimated = moneyAccounts.some((account) => account.balanceType === 'Estimated');
  const cardList = cards.data?.cards ?? [];
  const hasAnything = moneyAccounts.length > 0 || cardList.length > 0;

  const refresh = () => {
    void refetch();
    void cards.refetch();
  };

  return (
    <Screen refreshing={isRefetching || cards.isRefetching} onRefresh={refresh}>
      <View style={styles.header}>
        <View style={styles.headerRow}>
          <Typo variant="title">Tus cuentas</Typo>
          <Pressable onPress={() => router.push('/cuentas/importaciones')} hitSlop={12}>
            <Typo variant="caption" color={colors.textSecondary}>
              Historial
            </Typo>
          </Pressable>
        </View>
        {moneyAccounts.length > 0 ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {hidden ? '••••••' : formatCurrency(total)} es tu dinero
            {anyEstimated ? '  ·  incluye saldos estimados' : ''}
          </Typo>
        ) : null}
      </View>

      {isLoading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : !hasAnything ? (
        <>
          <EmptyState
            icon="wallet-outline"
            title="Aún no tienes cuentas"
            body="Agrega tu banco o billetera para centralizar tus movimientos."
            actionLabel="Agregar cuenta"
            onAction={() => router.push('/cuentas/agregar')}
          />
          <View style={styles.addSection}>
            <Button label="Agregar tarjeta de crédito" variant="secondary" onPress={() => router.push('/tarjetas/nueva')} />
          </View>
        </>
      ) : (
        <>
          <SectionHeader title="Mi dinero" />
          <View style={styles.stack}>
            {moneyAccounts.length === 0 ? (
              <Typo variant="body" color={colors.textSecondary}>
                Agrega tu banco o tu efectivo para saber cuánto dinero tienes.
              </Typo>
            ) : null}
            {moneyAccounts.map((account) => (
              <View key={account.id} style={styles.accountBlock}>
                <AccountCard
                  account={account}
                  hidden={hidden}
                  onPress={() => router.push(`/cuentas/conciliacion?accountId=${account.id}`)}
                  onUpdateBalance={() => router.push(`/cuentas/reconectar?accountId=${account.id}`)}
                />
                <View style={styles.accountActions}>
                  <View style={styles.grow}>
                    <Button
                      label="Importar movimientos"
                      variant="secondary"
                      compact
                      onPress={() => router.push(`/cuentas/importar?accountId=${account.id}`)}
                    />
                  </View>
                  <View style={styles.grow}>
                    <Button
                      label="Conciliar"
                      variant="secondary"
                      compact
                      onPress={() => router.push(`/cuentas/conciliacion?accountId=${account.id}`)}
                    />
                  </View>
                </View>
                <View style={styles.accountActions}>
                  <Button
                    label="Eliminar"
                    variant="ghost"
                    compact
                    fullWidth={false}
                    onPress={() => confirmDelete(account.id, account.alias)}
                  />
                </View>
              </View>
            ))}
          </View>

          <View style={styles.cardsSection}>
            <SectionHeader title="Tarjetas de crédito" />
            {cardList.length > 0 && cards.data ? (
              <Typo variant="caption" color={colors.textSecondary} style={styles.cardsTotals}>
                Deuda {hidden ? '••••••' : formatCurrency(cards.data.totalDebt)}
                {cards.data.totalNextPayments > 0
                  ? `  ·  próximos pagos ${hidden ? '••••' : formatCurrency(cards.data.totalNextPayments)}`
                  : ''}
              </Typo>
            ) : null}
            <View style={styles.stack}>
              {cardList.map((card) => (
                <CreditCardTile
                  key={card.id}
                  card={card}
                  hidden={hidden}
                  onPress={() =>
                    router.push({ pathname: '/tarjetas/[id]', params: { id: card.id, source: AnalyticsSource.Accounts } })
                  }
                />
              ))}
              {cardList.length === 0 ? (
                <Typo variant="body" color={colors.textSecondary}>
                  Registra tus tarjetas para saber cuánto debes, cuánto toca pagar y cuándo. Su cupo nunca se suma a tu dinero.
                </Typo>
              ) : null}
              <Button label="Agregar tarjeta" variant="secondary" onPress={() => router.push('/tarjetas/nueva')} />
            </View>
          </View>

          <View style={styles.addSection}>
            <SectionHeader title="Otra institución" />
            <Button label="Agregar cuenta" onPress={() => router.push('/cuentas/agregar')} />
          </View>
        </>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    marginBottom: spacing.xl,
    gap: 2,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  stack: {
    gap: spacing.xl,
  },
  accountBlock: {
    gap: spacing.md,
  },
  accountActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  grow: {
    flex: 1,
  },
  addSection: {
    marginTop: spacing.xxl,
  },
  cardsSection: {
    marginTop: spacing.xxl,
  },
  cardsTotals: {
    marginTop: -spacing.sm,
    marginBottom: spacing.md,
  },
});
