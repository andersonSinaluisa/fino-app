import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Typo } from '../ui';
import { formatRelativeTime } from '../../utils/format';
import type { StaleAccount } from '../../types/api';

interface StaleAccountsBannerProps {
  accounts: StaleAccount[];
}

/**
 * Entregable 15 ("Dashboard final MVP"): "cuentas desactualizadas" as its own
 * guaranteed dashboard section -- HomeSummaryDto.StaleAccounts lists every
 * manually-imported account that hasn't synced in a week (or ever), computed
 * by the backend every time. This does not replace the "Cuentas por
 * actualizar" insight card (which can still appear in "Para ti"); it just
 * guarantees the warning is visible even when that insight didn't win one of
 * the six card slots.
 */
export function StaleAccountsBanner({ accounts }: StaleAccountsBannerProps) {
  const router = useRouter();

  if (accounts.length === 0) {
    return null;
  }

  const [first, ...rest] = accounts;

  return (
    <View style={styles.card}>
      <View style={styles.header}>
        <Ionicons name="refresh-outline" size={17} color={colors.warning} />
        <Typo variant="bodyStrong">Cuentas por actualizar</Typo>
      </View>

      <Typo variant="caption" color={colors.textSecondary}>
        {first!.alias} no se actualiza desde {formatRelativeTime(first!.lastSyncedAt)}
        {rest.length > 0 ? ` y ${rest.length} cuenta${rest.length === 1 ? '' : 's'} más.` : '.'} Importa un estado de
        cuenta reciente para que tu saldo y tus movimientos sigan siendo confiables.
      </Typo>

      <View style={styles.action}>
        <Button
          label="Actualizar ahora"
          compact
          variant="secondary"
          fullWidth={false}
          onPress={() => router.push(`/cuentas/reconectar?accountId=${first!.accountId}`)}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.sm,
    marginTop: spacing.md,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  action: {
    alignItems: 'flex-start',
    marginTop: spacing.xs,
  },
});
