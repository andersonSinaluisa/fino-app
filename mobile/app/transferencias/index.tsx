import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useConfirmInternalTransfer, useInternalTransferCandidates } from '../../hooks/queries';
import { formatCurrency, formatFullDateTime } from '../../utils/format';
import type { InternalTransferCandidate } from '../../types/api';

function pairKey(candidate: InternalTransferCandidate): string {
  return `${candidate.outgoingTransactionId}:${candidate.incomingTransactionId}`;
}

export default function TransfersScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useInternalTransferCandidates();
  const confirm = useConfirmInternalTransfer();
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visible = (data ?? []).filter((candidate) => !dismissed.has(pairKey(candidate)));
  const primary = visible[0];

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Detección inteligente Nexo" tone="accent" />
        <Typo variant="title">Transferencias entre tus cuentas</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Confirmamos movimientos gemelos para que tus métricas reales de gasto e ingreso no se dupliquen.
        </Typo>
      </View>

      {isLoading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : !primary ? (
        <EmptyState
          icon="swap-horizontal-outline"
          title="Sin transferencias por conciliar"
          body="Cuando Nexo encuentre dos movimientos con monto opuesto y fechas cercanas, aparecerán aquí."
        />
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
              onPress={() =>
                confirm.mutate({
                  outgoingTransactionId: primary.outgoingTransactionId,
                  incomingTransactionId: primary.incomingTransactionId,
                })
              }
            />
            <Button
              label="No, son movimientos independientes"
              variant="secondary"
              onPress={() => setDismissed((current) => new Set(current).add(pairKey(primary)))}
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
