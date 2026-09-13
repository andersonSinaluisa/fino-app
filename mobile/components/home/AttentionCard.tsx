import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Typo } from '../ui/Typo';
import { formatCurrency, formatShortDate } from '../../utils/format';
import type { NextPaymentEstimate } from '../../utils/recurringPayments';
import type { StaleAccount } from '../../types/api';

interface AttentionCardProps {
  nextPayment: NextPaymentEstimate | null;
  staleAccounts: StaleAccount[];
  hidden: boolean;
}

const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * Rediseño de Home (2026-09), regla de UX #5: máximo una alerta/acción
 * prioritaria a la vez. "Próximo pago" (misma estimación que usan los
 * widgets del sistema, vía pickNextPayment) gana si hay uno; si no, y solo si
 * no lo hay, se muestra la cuenta desactualizada más urgente
 * (HomeSummaryDto.StaleAccounts, misma navegación que ya usaba
 * StaleAccountsBanner). Nunca ambas a la vez.
 */
export function AttentionCard({ nextPayment, staleAccounts, hidden }: AttentionCardProps) {
  const router = useRouter();

  if (nextPayment) {
    const daysUntil = Math.max(
      0,
      Math.ceil((nextPayment.estimatedDate.getTime() - Date.now()) / DAY_MS),
    );

    return (
      <Card>
        <Typo variant="overline" color={colors.textSecondary} style={styles.label}>
          Próximo
        </Typo>

        <View style={styles.row}>
          <View style={styles.body}>
            <Typo variant="bodyStrong" numberOfLines={1}>
              {nextPayment.payment.merchant}
            </Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              {formatShortDate(nextPayment.estimatedDate)} · En {daysUntil} día{daysUntil === 1 ? '' : 's'}
            </Typo>
          </View>
          <Typo variant="bodyStrong" tabular>
            {hidden ? '••••' : formatCurrency(nextPayment.payment.averageAmount)}
          </Typo>
        </View>

        <View style={styles.action}>
          <Button
            label="Ver próximos pagos"
            compact
            variant="secondary"
            fullWidth={false}
            onPress={() => router.push('/(tabs)/estadisticas')}
          />
        </View>
      </Card>
    );
  }

  const stale = staleAccounts[0];
  if (stale) {
    return (
      <Card>
        <View style={styles.headerRow}>
          <Ionicons name="alert-circle-outline" size={17} color={colors.warning} />
          <Typo variant="overline" color={colors.warning}>
            Requiere tu atención
          </Typo>
        </View>

        <Typo variant="bodyStrong" style={styles.staleAlias}>
          {stale.alias}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Tu información está desactualizada.
        </Typo>

        <View style={styles.action}>
          <Button
            label="Actualizar"
            compact
            variant="secondary"
            fullWidth={false}
            onPress={() => router.push(`/cuentas/reconectar?accountId=${stale.accountId}`)}
          />
        </View>
      </Card>
    );
  }

  return null;
}

const styles = StyleSheet.create({
  label: {
    marginBottom: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  body: {
    flex: 1,
    gap: 3,
  },
  action: {
    alignItems: 'flex-start',
    marginTop: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.sm,
  },
  staleAlias: {
    marginBottom: 2,
  },
});
