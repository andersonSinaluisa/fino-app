import { useMemo } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { ProviderAvatar } from '../../components/ui/ProviderAvatar';
import { useAccount, useImportHistory, useImportPreview, useTransactions } from '../../hooks/queries';
import { balanceTypeLabel, formatCurrency, formatRelativeTime, maskLabel } from '../../utils/format';
import type { ImportSummary, TransactionListItem } from '../../types/api';
import { transactionCategoryLabel } from '../../utils/splits';

export default function AccountReconciliationScreen() {
  const router = useRouter();
  const { accountId } = useLocalSearchParams<{ accountId: string }>();
  const { data: account, isLoading: accountLoading, refetch: refetchAccount, isRefetching } = useAccount(accountId);
  const { data: transactionsData } = useTransactions({ accountId });
  const { data: importsData } = useImportHistory();

  const transactions = useMemo(
    () => transactionsData?.pages.flatMap((page) => page.items) ?? [],
    [transactionsData],
  );
  const latestImport = useMemo(
    () => findLatestCompletedImport(importsData?.pages.flatMap((page) => page.items) ?? [], accountId),
    [importsData, accountId],
  );
  const { data: preview } = useImportPreview(latestImport?.importId);

  const needsReview = transactions.filter((transaction) => transaction.status === 'NeedsReview');
  const declaredBalance = preview?.declaredClosingBalance ?? null;
  const difference = declaredBalance === null || !account ? null : account.balance - declaredBalance;
  const hasDifference = difference !== null && Math.abs(difference) >= 0.01;

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetchAccount()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Conciliación activa" tone={hasDifference ? 'attention' : 'positive'} />
        <Typo variant="title">Conciliación de cuenta</Typo>
      </View>

      {accountLoading || !account ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : (
        <>
          <Card style={styles.accountCard}>
            <ProviderAvatar name={account.providerName} color={account.brandColor} />
            <View style={styles.flex}>
              <Typo variant="heading">{account.providerName}</Typo>
              <Typo variant="caption" color={colors.textSecondary}>
                {[account.alias, maskLabel(account.mask), account.currency].filter(Boolean).join(' · ')}
              </Typo>
            </View>
            <Badge label={balanceTypeLabel(account.balanceType)} tone={account.balanceType === 'Verified' ? 'positive' : 'neutral'} />
          </Card>

          <Card tone={hasDifference ? 'ink' : 'accent'} style={styles.hero}>
            <View style={styles.inline}>
              <Ionicons
                name={hasDifference ? 'alert-circle-outline' : 'checkmark-circle-outline'}
                size={18}
                color={hasDifference ? colors.accent : colors.text}
              />
              <Typo variant="bodyStrong" color={hasDifference ? colors.accent : colors.text}>
                {hasDifference ? 'Atención sugerida' : 'Todo al día'}
              </Typo>
            </View>
            <Typo variant="heading" color={hasDifference ? colors.onPrimary : colors.text}>
              {hasDifference
                ? `Encontramos una diferencia de ${formatCurrency(Math.abs(difference))}`
                : 'No hay diferencias claras con la información disponible'}
            </Typo>
            <Typo variant="body" color={hasDifference ? colors.onPrimary : colors.textSecondary}>
              {declaredBalance === null
                ? 'El último archivo importado no trae saldo final declarado; usa el saldo verificado o importa un extracto reciente.'
                : 'Comparamos el saldo calculado por Nexo contra el saldo final declarado por el extracto.'}
            </Typo>
          </Card>

          <View style={styles.section}>
            <SectionHeader title="Comparación de saldos" />
            <Card style={styles.stack}>
              <BalanceLine label="Según tus registros en Nexo" value={account.balance} />
              <BalanceLine label="Extracto oficial del banco" value={declaredBalance} />
              <BalanceLine label="Descuadre actual" value={difference} highlight={hasDifference} />
              {latestImport ? (
                <Typo variant="caption" color={colors.textSecondary}>
                  Último extracto: {latestImport.fileName} · {formatRelativeTime(latestImport.createdAt)}
                </Typo>
              ) : null}
            </Card>
          </View>

          <View style={styles.section}>
            <SectionHeader title="Movimientos por revisar" />
            {needsReview.length === 0 ? (
              <EmptyState icon="checkmark-done-outline" title="Sin pendientes" body="No hay movimientos marcados para revisión en esta cuenta." />
            ) : (
              <View style={styles.stack}>
                {needsReview.slice(0, 4).map((transaction) => (
                  <ReviewRow
                    key={transaction.id}
                    transaction={transaction}
                    onPress={() => router.push(`/movimiento/${transaction.id}`)}
                  />
                ))}
              </View>
            )}
          </View>

          <View style={styles.actions}>
            <Button label="Actualizar saldo verificado" variant="secondary" onPress={() => router.push(`/cuentas/actualizar-saldo?accountId=${account.id}`)} />
            <Button label="Importar nuevo estado de cuenta" onPress={() => router.push(`/cuentas/importar?accountId=${account.id}`)} />
          </View>
        </>
      )}
    </Screen>
  );
}

function findLatestCompletedImport(imports: ImportSummary[], accountId: string | undefined): ImportSummary | undefined {
  return imports.find((item) => item.financialAccountId === accountId && item.status === 'Completed');
}

function BalanceLine({
  label,
  value,
  highlight = false,
}: {
  label: string;
  value: number | null;
  highlight?: boolean;
}) {
  return (
    <View style={styles.balanceLine}>
      <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
        {label}
      </Typo>
      <Typo variant="bodyStrong" color={highlight ? colors.danger : colors.text} tabular>
        {value === null ? 'Sin dato' : formatCurrency(value, { signed: label.includes('Descuadre') })}
      </Typo>
    </View>
  );
}

function ReviewRow({ transaction, onPress }: { transaction: TransactionListItem; onPress: () => void }) {
  return (
    <Card onPress={onPress} style={styles.reviewRow}>
      <View style={styles.flex}>
        <Typo variant="bodyStrong" numberOfLines={1}>
          {transaction.merchant ?? transaction.description}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {transactionCategoryLabel(transaction)} · Posible duplicado
        </Typo>
      </View>
      <Typo variant="bodyStrong" tabular>
        {formatCurrency(transaction.signedAmount, { signed: true })}
      </Typo>
    </Card>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
  stack: {
    gap: spacing.md,
  },
  accountCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  hero: {
    gap: spacing.md,
    marginTop: spacing.lg,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  section: {
    marginTop: spacing.xxl,
  },
  balanceLine: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  reviewRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  actions: {
    gap: spacing.md,
    marginTop: spacing.xxl,
  },
});
