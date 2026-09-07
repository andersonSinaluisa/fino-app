import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { AccountCard } from '../../components/accounts/AccountCard';
import { useAccounts, useDeleteFinancialAccount } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { formatCurrency } from '../../utils/format';

export default function AccountsScreen() {
  const router = useRouter();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data, isLoading, refetch, isRefetching } = useAccounts();
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

  const total = (data ?? []).reduce((sum, account) => sum + account.balance, 0);
  const anyEstimated = (data ?? []).some((account) => account.balanceType === 'Estimated');

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <View style={styles.header}>
        <View style={styles.headerRow}>
          <Typo variant="title">Tus cuentas</Typo>
          <Pressable onPress={() => router.push('/cuentas/importaciones')} hitSlop={12}>
            <Typo variant="caption" color={colors.textSecondary}>
              Historial
            </Typo>
          </Pressable>
        </View>
        {data && data.length > 0 ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {hidden ? '••••••' : formatCurrency(total)} en total
            {anyEstimated ? '  ·  incluye saldos estimados' : ''}
          </Typo>
        ) : null}
      </View>

      {isLoading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : !data || data.length === 0 ? (
        <EmptyState
          icon="wallet-outline"
          title="Aún no tienes cuentas"
          body="Agrega tu banco o billetera para centralizar tus movimientos."
          actionLabel="Agregar cuenta"
          onAction={() => router.push('/cuentas/agregar')}
        />
      ) : (
        <>
          <View style={styles.stack}>
            {data.map((account) => (
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
});
