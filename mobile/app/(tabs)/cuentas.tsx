import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { AccountCard } from '../../components/accounts/AccountCard';
import { useAccounts } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { formatCurrency } from '../../utils/format';

export default function AccountsScreen() {
  const router = useRouter();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data, isLoading, refetch, isRefetching } = useAccounts();

  const total = (data ?? []).reduce((sum, account) => sum + account.balance, 0);
  const anyEstimated = (data ?? []).some((account) => account.balanceType === 'Estimated');

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <View style={styles.header}>
        <Typo variant="title">Tus cuentas</Typo>
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
                <AccountCard account={account} hidden={hidden} />
                <Button
                  label="Importar movimientos"
                  variant="secondary"
                  compact
                  onPress={() => router.push(`/cuentas/importar?accountId=${account.id}`)}
                />
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
  stack: {
    gap: spacing.xl,
  },
  accountBlock: {
    gap: spacing.md,
  },
  addSection: {
    marginTop: spacing.xxl,
  },
});
