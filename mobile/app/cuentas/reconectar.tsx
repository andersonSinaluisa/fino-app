import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Badge, Button, Card, Screen, SkeletonCard, Typo } from '../../components/ui';
import { ProviderAvatar } from '../../components/ui/ProviderAvatar';
import { useAccount } from '../../hooks/queries';
import { formatCurrency, formatRelativeTime, maskLabel } from '../../utils/format';

export default function ReconnectAccountScreen() {
  const router = useRouter();
  const { accountId } = useLocalSearchParams<{ accountId: string }>();
  const { data: account, isLoading, refetch, isRefetching } = useAccount(accountId);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Mantenimiento seguro" tone="attention" />
        <Typo variant="title">Actualiza tu cuenta</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Tus datos anteriores están intactos. En local, la vía real disponible es importar un extracto o confirmar saldo.
        </Typo>
      </View>

      {isLoading || !account ? (
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
                {[account.alias, maskLabel(account.mask)].filter(Boolean).join(' · ')}
              </Typo>
            </View>
          </Card>

          <Card tone="secondary" style={styles.stack}>
            <InfoRow label="Último saldo registrado" value={formatCurrency(account.balance)} />
            <InfoRow label="Última actualización" value={formatRelativeTime(account.lastSyncedAt ?? account.lastTransactionAt)} />
          </Card>

          <Card style={styles.stack}>
            <View style={styles.inline}>
              <Ionicons name="shield-checkmark-outline" size={18} color={colors.text} />
              <Typo variant="bodyStrong">Modo lectura exclusivo</Typo>
            </View>
            <Typo variant="caption" color={colors.textSecondary}>
              Nexo no puede emitir transferencias ni pagos. Solo registra movimientos importados o detectados por canales configurados.
            </Typo>
          </Card>

          <View style={styles.actions}>
            <Button
              label="Reconectar banco"
              variant="secondary"
              onPress={() =>
                Alert.alert(
                  'Conexión directa no disponible',
                  'Este backend local no tiene endpoint ni credenciales reales de Open Banking. Usa importación de archivo para actualizar esta cuenta.',
                )
              }
            />
            <Button label="Cargar extracto digital" onPress={() => router.push(`/cuentas/importar?accountId=${account.id}`)} />
            <Button label="Actualizar saldo manualmente" variant="ghost" onPress={() => router.push(`/cuentas/actualizar-saldo?accountId=${account.id}`)} />
          </View>
        </>
      )}
    </Screen>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.infoRow}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="bodyStrong" tabular>
        {value}
      </Typo>
    </View>
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
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  infoRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  actions: {
    gap: spacing.md,
    marginTop: spacing.xxl,
  },
});
