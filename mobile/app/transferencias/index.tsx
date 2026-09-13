import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useConfirmInternalTransfer, useInternalTransferCandidates, useWithdrawalCandidates } from '../../hooks/queries';
import { WithdrawalCard } from '../../components/transactions/WithdrawalCard';
import { formatCurrency, formatFullDateTime } from '../../utils/format';
import type { InternalTransferCandidate } from '../../types/api';
import { AnalyticsEvent, AnalyticsSource, toCountBucket, track } from '../../services/analytics';

function pairKey(candidate: InternalTransferCandidate): string {
  return `${candidate.outgoingTransactionId}:${candidate.incomingTransactionId}`;
}

export default function TransfersScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useInternalTransferCandidates();
  const { data: withdrawals, isLoading: withdrawalsLoading } = useWithdrawalCandidates();
  const confirm = useConfirmInternalTransfer();
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visible = (data ?? []).filter((candidate) => !dismissed.has(pairKey(candidate)));
  const primary = visible[0];

  // §9: una sola bandeja "Por revisar" para todo lo que espera una decisión. Los
  // retiros no tienen pantalla propia porque son transferencias internas: separarlos
  // obligaría a la persona a saber de antemano qué tipo de duda tiene Fino.
  const pendingWithdrawals = withdrawals ?? [];
  const pendingCount = visible.length + pendingWithdrawals.length;
  const loading = isLoading || withdrawalsLoading;

  // §25 (Dashboard 6): el denominador del embudo de conciliación. Sin saber
  // cuántas sugerencias vio la persona, "confirmó 3" no significa nada.
  useEffect(() => {
    if (loading) {
      return;
    }

    track(AnalyticsEvent.ReconciliationViewed, {
      source: AnalyticsSource.Accounts,
      suggestionCountBucket: toCountBucket(pendingCount),
    });

    if (primary) {
      // El backend todavía no devuelve una confianza para los pares de
      // transferencia (InternalTransferCandidate no la tiene, a diferencia de
      // WithdrawalCandidate). El evento se manda igual: el embudo
      // mostrada -> confirmada/rechazada se puede calcular sin esa dimensión,
      // y el día que el backend la exponga se añade aquí sin tocar el resto.
      track(AnalyticsEvent.TransferSuggestionShown);
    }
    // Solo cuando cambia lo que hay por revisar, no en cada render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, pendingCount]);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge
          label={pendingCount > 0 ? `Por revisar · ${pendingCount}` : 'Por revisar'}
          tone="accent"
        />
        <Typo variant="title">Movimientos por revisar</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Mover dinero entre tus propias cuentas no es gastarlo. Confirma estos movimientos para que
          tus gastos reales no se inflen.
        </Typo>
      </View>

      {/* Los retiros van primero: son la duda más frecuente y la que más distorsiona
          las estadísticas si se deja sin resolver. */}
      {pendingWithdrawals.length > 0 ? (
        <View style={styles.stack}>
          {pendingWithdrawals.map((candidate) => (
            <WithdrawalCard key={candidate.transactionId} candidate={candidate} />
          ))}
        </View>
      ) : null}

      {loading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : !primary ? (
        pendingWithdrawals.length > 0 ? null : (
          <EmptyState
            icon="checkmark-done-outline"
            title="Nada por revisar"
            body="Cuando Fino detecte un retiro en efectivo o dos movimientos gemelos entre tus cuentas, aparecerán aquí."
          />
        )
      ) : (
        <>
          <TransferCard candidate={primary} />

          <Card tone="secondary" style={styles.stack}>
            <View style={styles.inline}>
              <Ionicons name="shield-checkmark-outline" size={18} color={colors.text} />
              <Typo variant="subheading">¿Qué sucede al confirmar?</Typo>
            </View>
            <Explanation text="No contará como gasto ni como ingreso; tu patrimonio total no cambió." />
            <Explanation text="Los reportes del mes quedan limpios y sin falsos picos." />
            <Explanation text="Cada movimiento conserva su comprobante y queda enlazado con el otro lado." />
          </Card>

          <View style={styles.actions}>
            <Button
              label="Sí, es una transferencia"
              loading={confirm.isPending}
              onPress={() => {
                track(AnalyticsEvent.TransferConfirmed, { source: AnalyticsSource.Accounts });
                confirm.mutate({
                  outgoingTransactionId: primary.outgoingTransactionId,
                  incomingTransactionId: primary.incomingTransactionId,
                });
              }}
            />
            <Button
              label="No, son movimientos independientes"
              variant="secondary"
              onPress={() => {
                track(AnalyticsEvent.TransferRejected, { source: AnalyticsSource.Accounts });
                setDismissed((current) => new Set(current).add(pairKey(primary)));
              }}
            />
          </View>

          {visible.length > 1 ? (
            <View style={styles.section}>
              <Typo variant="subheading">Otras transferencias por conciliar</Typo>
              {visible.slice(1).map((candidate) => (
                <Pressable key={pairKey(candidate)} onPress={() => setDismissed(new Set())}>
                  <Card style={styles.otherRow}>
                    <Ionicons name="swap-horizontal-outline" size={18} color={colors.text} />
                    <View style={styles.flex}>
                      <Typo variant="bodyStrong" numberOfLines={1}>
                        {candidate.outgoingAccountAlias} hacia {candidate.incomingAccountAlias}
                      </Typo>
                      <Typo variant="caption" color={colors.textSecondary}>
                        {formatCurrency(candidate.amount)} · {formatFullDateTime(candidate.outgoingDate)}
                      </Typo>
                    </View>
                  </Card>
                </Pressable>
              ))}
            </View>
          ) : null}
        </>
      )}
    </Screen>
  );
}

function TransferCard({ candidate }: { candidate: InternalTransferCandidate }) {
  return (
    <Card style={styles.transferCard}>
      <View style={styles.transferHeader}>
        <Badge label="Coincidencia automática" tone="positive" />
        <Typo variant="caption" color={colors.textSecondary}>
          Ref #{candidate.outgoingTransactionId.slice(0, 8)}
        </Typo>
      </View>

      <Typo variant="overline" align="center" color={colors.textSecondary}>
        MONTO TRANSFERIDO
      </Typo>
      <Typo variant="display" align="center" tabular>
        {formatCurrency(candidate.amount)}
      </Typo>

      <Leg
        role="Origen"
        account={candidate.outgoingAccountAlias}
        description={candidate.outgoingDescription}
        date={candidate.outgoingDate}
        amount={-candidate.amount}
      />
      <View style={styles.bridge}>
        <View style={styles.bridgeLine} />
        <View style={styles.bridgeBadge}>
          <Ionicons name="sync-outline" size={15} color={colors.onAccent} />
        </View>
        <View style={styles.bridgeLine} />
      </View>
      <Leg
        role="Destino"
        account={candidate.incomingAccountAlias}
        description={candidate.incomingDescription}
        date={candidate.incomingDate}
        amount={candidate.amount}
      />
    </Card>
  );
}

function Leg({
  role,
  account,
  description,
  date,
  amount,
}: {
  role: string;
  account: string;
  description: string;
  date: string;
  amount: number;
}) {
  return (
    <View style={styles.leg}>
      <Badge label={role} tone={amount > 0 ? 'positive' : 'neutral'} />
      <View style={styles.flex}>
        <Typo variant="bodyStrong">{account}</Typo>
        <Typo variant="caption" color={colors.textSecondary} numberOfLines={2}>
          {formatFullDateTime(date)} · {description}
        </Typo>
      </View>
      <Typo variant="bodyStrong" color={amount > 0 ? colors.success : colors.text} tabular>
        {formatCurrency(amount, { signed: true })}
      </Typo>
    </View>
  );
}

function Explanation({ text }: { text: string }) {
  return (
    <View style={styles.inline}>
      <Ionicons name="checkmark-circle-outline" size={17} color={colors.success} />
      <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
        {text}
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
  transferCard: {
    gap: spacing.lg,
  },
  transferHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  leg: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  bridge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  bridgeLine: {
    flex: 1,
    height: 1,
    backgroundColor: colors.borderStrong,
  },
  bridgeBadge: {
    width: 30,
    height: 30,
    borderRadius: 15,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  actions: {
    gap: spacing.md,
    marginTop: spacing.xxl,
  },
  section: {
    marginTop: spacing.xxl,
    gap: spacing.md,
  },
  otherRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
});
