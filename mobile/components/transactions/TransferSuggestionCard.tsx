import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Typo } from '../ui';
import { useConfirmInternalTransfer, useInternalTransferCandidates, useWithdrawalCandidates } from '../../hooks/queries';
import { WithdrawalCard } from './WithdrawalCard';
import { formatCurrency } from '../../utils/format';
import type { InternalTransferCandidate } from '../../types/api';

function pairKey(candidate: InternalTransferCandidate): string {
  return `${candidate.outgoingTransactionId}:${candidate.incomingTransactionId}`;
}

/**
 * Entregable 13: "¿Esto fue una transferencia entre tus cuentas?" -- shown above
 * the movements list whenever Fino finds a pair that looks like the person's own
 * money crossing accounts (same amount, opposite direction, a few days apart).
 * Dismissing one only hides it for this session: there is no persistent "never
 * suggest this pair again" yet (documented as pending in the Entregable 13
 * report), so it can reappear next time the screen mounts.
 */
export function TransferSuggestionCard() {
  const { data: candidates } = useInternalTransferCandidates();
  const { data: withdrawals } = useWithdrawalCandidates();
  const confirm = useConfirmInternalTransfer();
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const visible = (candidates ?? []).filter((candidate) => !dismissed.has(pairKey(candidate)));

  // Los retiros van antes que las transferencias entre bancos: son mucho más
  // frecuentes y, sin conciliar, distorsionan más las estadísticas (un retiro sin
  // revisar infla el gasto del mes con dinero que la persona todavía tiene).
  // Se muestra UNO, no una pila: esta tarjeta vive encima de la lista de
  // movimientos, y convertirla en una bandeja la haría inutilizable.
  const withdrawal = (withdrawals ?? [])[0];

  if (withdrawal) {
    return <WithdrawalCard candidate={withdrawal} />;
  }

  if (visible.length === 0) {
    return null;
  }

  const candidate = visible[0]!;
  const key = pairKey(candidate);

  return (
    <View style={styles.card}>
      <View style={styles.header}>
        <Ionicons name="swap-horizontal" size={18} color={colors.accent} />
        <Typo variant="bodyStrong">¿Esto fue una transferencia entre tus cuentas?</Typo>
      </View>

      <Typo variant="caption" color={colors.textSecondary} style={styles.body}>
        {candidate.outgoingAccountAlias} envió {formatCurrency(candidate.amount)} y {candidate.incomingAccountAlias}{' '}
        recibió el mismo monto poco después. Si es así, no la contaremos como gasto ni como ingreso.
      </Typo>

      <View style={styles.actions}>
        <Button
          label="Sí, es una transferencia"
          compact
          loading={confirm.isPending}
          onPress={() =>
            confirm.mutate({
              outgoingTransactionId: candidate.outgoingTransactionId,
              incomingTransactionId: candidate.incomingTransactionId,
            })
          }
        />
        <Button
          label="No"
          compact
          variant="ghost"
          onPress={() => setDismissed((prev) => new Set(prev).add(key))}
        />
      </View>

      {visible.length > 1 ? (
        <Typo variant="caption" color={colors.textSecondary} style={styles.more}>
          +{visible.length - 1} sugerencia{visible.length - 1 === 1 ? '' : 's'} más
        </Typo>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: spacing.xl,
    marginBottom: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  body: {
    lineHeight: 18,
  },
  actions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
  more: {
    marginTop: spacing.xs,
  },
});
