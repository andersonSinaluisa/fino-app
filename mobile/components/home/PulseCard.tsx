import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Typo } from '../ui/Typo';
import { formatCurrency, formatShortDate } from '../../utils/format';
import type { NextPaymentEstimate } from '../../utils/recurringPayments';
import type { Pulse } from '../../types/api';
import { AnalyticsEvent, AnalyticsSource, toAnalyticsSymbol, track } from '../../services/analytics';

interface PulseCardProps {
  /** Ya ordenados por relevancia (usePulses) -- se muestra a lo sumo el primero. */
  pulses: Pulse[] | undefined;
  /** Reglas #5 de UX de Home: "¿algo requiere mi atención?" nunca se responde dos veces.
   *  PulseEngine todavía no tiene una regla de "próximo pago" (esa es UpcomingCommitment,
   *  pendiente), así que mientras tanto se conserva el comportamiento de AttentionCard: si
   *  no hay un pulso real que mostrar, cae a este próximo pago calculado en el cliente. */
  nextPayment: NextPaymentEstimate | null;
  hidden: boolean;
}

const DAY_MS = 24 * 60 * 60 * 1000;

const iconFor: Record<Pulse['type'], keyof typeof Ionicons.glyphMap> = {
  SpendingPace: 'speedometer-outline',
  UnusualExpense: 'alert-circle-outline',
  BalanceChange: 'swap-vertical-outline',
  UpcomingCommitment: 'calendar-outline',
  MonthEndProjection: 'trending-up-outline',
  SpendingImprovement: 'trophy-outline',
  CategorySpike: 'flame-outline',
  NewIncome: 'cash-outline',
  DailyClose: 'moon-outline',
  AccountOutdated: 'refresh-outline',
};

const accentFor: Record<Pulse['severity'], string> = {
  Positive: colors.accentSecondary,
  Attention: colors.warning,
  Risk: colors.danger,
  Neutral: colors.textSecondary,
};

const overlineFor: Record<Pulse['severity'], string> = {
  Positive: 'Algo positivo',
  Attention: 'Requiere tu atención',
  Risk: 'Importante',
  Neutral: 'Novedad',
};

/**
 * PULSO FASE 2: reemplaza a AttentionCard como la única respuesta de Home a
 * "¿algo requiere mi atención?" (regla de UX #5: como mucho una alerta a la
 * vez). Prioridad: el pulso más relevante que ya calculó PulseEngine en el
 * servidor (relevanceScore, orden que ya trae usePulses) > si no hay
 * ninguno, el próximo pago estimado que ya mostraba AttentionCard > si
 * tampoco hay eso, nada. Las cuentas desactualizadas ya las cubre PulseEngine
 * (AccountOutdated) del lado del servidor cuando corresponde, así que ya no
 * hace falta pasarle staleAccounts a esta card.
 */
export function PulseCard({ pulses, nextPayment, hidden }: PulseCardProps) {
  const router = useRouter();
  const top = pulses && pulses.length > 0 ? pulses[0] : null;

  /**
   * §25 (Dashboard 5): el DENOMINADOR de PULSE_OPEN_RATE.
   *
   * Sin este evento, "se abrieron 40 pulsos" no significa nada: no se puede
   * distinguir 40 de 50 mostrados (buenísimo) de 40 de 4.000 (ruido). Se
   * emite cuando la tarjeta de verdad tiene un pulso que pintar, no cuando el
   * componente se monta.
   */
  const topKind = toAnalyticsSymbol(top?.type);

  useEffect(() => {
    if (!topKind) {
      return;
    }

    track(AnalyticsEvent.PulseCardViewed, { pulseKind: topKind, source: AnalyticsSource.Home });
  }, [topKind]);

  if (top) {
    const accent = accentFor[top.severity];

    return (
      <Card>
        <View style={styles.headerRow}>
          <View style={[styles.icon, { backgroundColor: `${accent}33` }]}>
            <Ionicons name={iconFor[top.type] ?? 'sparkles-outline'} size={16} color={colors.text} />
          </View>
          <Typo variant="overline" color={accent}>
            {overlineFor[top.severity]}
          </Typo>
        </View>

        <Typo variant="bodyStrong" numberOfLines={2} style={styles.title}>
          {top.title}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary} numberOfLines={2}>
          {top.body}
        </Typo>

        <View style={styles.action}>
          <Button label="Ver detalle" compact variant="secondary" fullWidth={false} onPress={() => {
            track(AnalyticsEvent.PulseActionClicked, {
              pulseKind: toAnalyticsSymbol(top.type),
              actionKind: 'view_detail',
            });
            router.push(`/pulso/${top.id}`);
          }} />
        </View>
      </Card>
    );
  }

  if (nextPayment) {
    const daysUntil = Math.max(
      0,
      Math.ceil((nextPayment.estimatedDate.getTime() - Date.now()) / DAY_MS),
    );

    return (
      <Card>
        <Typo variant="overline" color={colors.textSecondary} style={styles.title}>
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

  return null;
}

const styles = StyleSheet.create({
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.sm,
  },
  icon: {
    width: 28,
    height: 28,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
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
});
