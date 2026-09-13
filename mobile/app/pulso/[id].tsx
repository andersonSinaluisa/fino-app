import { useEffect } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Badge, Card, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { usePulse, useSubmitPulseFeedback } from '../../hooks/queries';
import { formatCurrency, formatFullDateTime, formatPeriodChange } from '../../utils/format';
import type { Pulse } from '../../types/api';
import { AnalyticsEvent, AnalyticsSource, toAnalyticsSymbol, track, trackOnce } from '../../services/analytics';

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

const badgeToneFor: Record<Pulse['severity'], 'positive' | 'attention' | 'danger' | 'neutral'> = {
  Positive: 'positive',
  Attention: 'attention',
  Risk: 'danger',
  Neutral: 'neutral',
};

const severityLabel: Record<Pulse['severity'], string> = {
  Positive: 'Algo positivo',
  Attention: 'Requiere tu atención',
  Risk: 'Importante',
  Neutral: 'Novedad',
};

/**
 * PULSO FASE 2: pantalla de detalle de un pulso, abierta desde la card de
 * Home o desde "Actividad de FINO". Estructura pedida por el diseño de
 * PULSO: QUÉ PASÓ (título + descripción), QUÉ MOVIMIENTO (los números reales
 * que generaron el pulso, cuando los hay) y "¿Por qué veo esto?" (la
 * explicación de PulseEngine -- nunca en blanco, ver PulsesTests). Todo el
 * contenido viene de GET /pulses/{id}; nada se inventa ni se calcula aquí.
 * PULSO FASE 4 agrega el 👍/👎 al final -- puramente señal de producto, nunca
 * leído por PulseEngine ni usado para nada dentro de la app misma.
 */
export default function PulseDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data, isLoading, isError } = usePulse(id);
  const submitFeedback = useSubmitPulseFeedback();

  /**
   * §25 (Dashboard 5): la mitad final de PULSE_OPEN_RATE. Viaja el TIPO de
   * pulso, que es un enum del dominio -- nunca su título ni su cuerpo, que
   * llevan montos y nombres de categoría.
   */
  const pulseKind = toAnalyticsSymbol(data?.type);

  useEffect(() => {
    if (!pulseKind) {
      return;
    }

    track(AnalyticsEvent.PulseOpened, { pulseKind, source: AnalyticsSource.Unknown });
    void trackOnce(AnalyticsEvent.FirstPulseOpened, { pulseKind });
  }, [pulseKind]);

  if (isError) {
    return (
      <Screen>
        <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
          <Ionicons name="chevron-back" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Volver
          </Typo>
        </Pressable>
        <EmptyState
          icon="alert-circle-outline"
          title="No encontramos este pulso"
          body="Puede que ya no exista, o que pertenezca a otra cuenta."
        />
      </Screen>
    );
  }

  if (isLoading || !data) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const accent = accentFor[data.severity];
  const change = formatPeriodChange(data.percentChange, 'la comparación');
  const hasMovement = data.value !== null;

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.headerBlock}>
        <View style={styles.headerRow}>
          <View style={[styles.icon, { backgroundColor: `${accent}33` }]}>
            <Ionicons name={iconFor[data.type] ?? 'sparkles-outline'} size={18} color={colors.text} />
          </View>
          <Badge label={severityLabel[data.severity]} tone={badgeToneFor[data.severity]} />
        </View>

        <Typo variant="heading" style={styles.title}>
          {data.title}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {formatFullDateTime(data.occurredAt)}
        </Typo>
      </View>

      <View style={styles.section}>
        <SectionHeader title="Qué pasó" />
        <Card>
          <Typo variant="body">{data.body}</Typo>
        </Card>
      </View>

      {hasMovement ? (
        <View style={styles.section}>
          <SectionHeader title="Qué movimiento" />
          <Card>
            <View style={styles.movementRow}>
              <Typo variant="caption" color={colors.textSecondary}>
                Valor
              </Typo>
              <Typo variant="bodyStrong" tabular>
                {formatCurrency(data.value!)}
              </Typo>
            </View>

            {data.comparisonValue !== null ? (
              <>
                <View style={styles.divider} />
                <View style={styles.movementRow}>
                  <Typo variant="caption" color={colors.textSecondary}>
                    Antes
                  </Typo>
                  <Typo variant="body" tabular>
                    {formatCurrency(data.comparisonValue)}
                  </Typo>
                </View>
              </>
            ) : null}

            {change ? (
              <>
                <View style={styles.divider} />
                <View style={styles.movementRow}>
                  <Typo variant="caption" color={colors.textSecondary}>
                    Cambio
                  </Typo>
                  <Typo variant="body" color={change.up ? colors.danger : colors.success}>
                    {change.up ? '+' : '-'}
                    {change.label.split(' ')[0]}
                  </Typo>
                </View>
              </>
            ) : null}
          </Card>
        </View>
      ) : null}

      <View style={styles.section}>
        <SectionHeader title="¿Por qué veo esto?" />
        <Card tone="secondary">
          <Typo variant="body" color={colors.textSecondary}>
            {data.explanation}
          </Typo>
        </Card>
      </View>

      <View style={styles.section}>
        <Card>
          <View style={styles.feedbackRow}>
            <Typo variant="caption" color={colors.textSecondary} style={styles.feedbackLabel}>
              ¿Te sirvió este aviso?
            </Typo>
            <View style={styles.feedbackButtons}>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Sí, me sirvió"
                accessibilityState={{ selected: data.feedbackHelpful === true }}
                onPress={() => {
                  track(AnalyticsEvent.PulseFeedback, {
                    pulseKind: toAnalyticsSymbol(data.type),
                    sentiment: 'helpful',
                  });
                  submitFeedback.mutate({ id: data.id, helpful: true });
                }}
                style={styles.feedbackButton}
              >
                <Ionicons
                  name={data.feedbackHelpful === true ? 'thumbs-up' : 'thumbs-up-outline'}
                  size={18}
                  color={data.feedbackHelpful === true ? colors.accentSecondary : colors.textSecondary}
                />
              </Pressable>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="No, no me sirvió"
                accessibilityState={{ selected: data.feedbackHelpful === false }}
                onPress={() => {
                  track(AnalyticsEvent.PulseFeedback, {
                    pulseKind: toAnalyticsSymbol(data.type),
                    sentiment: 'not_helpful',
                  });
                  submitFeedback.mutate({ id: data.id, helpful: false });
                }}
                style={styles.feedbackButton}
              >
                <Ionicons
                  name={data.feedbackHelpful === false ? 'thumbs-down' : 'thumbs-down-outline'}
                  size={18}
                  color={data.feedbackHelpful === false ? colors.danger : colors.textSecondary}
                />
              </Pressable>
            </View>
          </View>
        </Card>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  headerBlock: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.sm,
  },
  icon: {
    width: 32,
    height: 32,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
    marginTop: spacing.xs,
  },
  section: {
    marginTop: spacing.xxl,
  },
  movementRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.lg,
    paddingVertical: spacing.sm,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  feedbackRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  feedbackLabel: {
    flex: 1,
  },
  feedbackButtons: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  feedbackButton: {
    width: 36,
    height: 36,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surfaceSecondary,
  },
});
