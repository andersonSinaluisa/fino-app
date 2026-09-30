import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Skeleton, Typo } from '../ui';
import { formatCurrency } from '../../utils/format';
import type { CommittedMoney, HomeSummary } from '../../types/api';

interface AvailableMoneySectionProps {
  summary: HomeSummary;
  /** GET /finance/committed. `undefined` mientras carga. */
  committed: CommittedMoney | undefined;
  hidden: boolean;
  onOpenCommitted: () => void;
}

/**
 * "Disponible ahora": Tu dinero − Comprometido. Los tres números salen tal
 * cual de GET /finance/committed (CommittedMoneyCalculator en el backend); la
 * app ya no proyecta nada por su cuenta, así que Home, el desglose y la
 * previsualización de un presupuesto no pueden decir cosas distintas.
 *
 * "Comprometido" se puede tocar: lleva al desglose por origen, porque un
 * número sin explicación en una app de dinero genera desconfianza.
 */
export function AvailableMoneySection({ summary, committed, hidden, onOpenCommitted }: AvailableMoneySectionProps) {
  if (summary.accountCount === 0) {
    return null;
  }

  if (!committed) {
    return (
      <View style={styles.wrapper}>
        <Typo variant="caption" color={colors.textSecondary}>
          Disponible ahora
        </Typo>
        <Skeleton height={34} width="55%" />
      </View>
    );
  }

  const hasCommitted = committed.committed > 0;

  return (
    <View style={styles.wrapper}>
      <Typo variant="caption" color={colors.textSecondary}>
        Disponible ahora
      </Typo>
      <Typo variant="title" tabular>
        {formatCurrency(committed.available, { hidden })}
      </Typo>

      {hasCommitted ? (
        <View style={styles.breakdown}>
          <BreakdownRow label="Tu dinero" value={committed.currentMoney} hidden={hidden} />
          <Pressable
            onPress={onOpenCommitted}
            accessibilityRole="button"
            accessibilityLabel={`Comprometido ${formatCurrency(committed.committed, { hidden })}`}
            accessibilityHint="Muestra de dónde sale el dinero comprometido"
            style={({ pressed }) => [styles.row, styles.pressableRow, pressed ? styles.pressed : null]}
          >
            <View style={styles.inline}>
              <Typo variant="caption" color={colors.textSecondary}>
                Comprometido
              </Typo>
              <Ionicons name="information-circle-outline" size={14} color={colors.textSecondary} />
            </View>
            <View style={styles.inline}>
              <Typo variant="body" tabular color={colors.textSecondary}>
                {formatCurrency(committed.committed, { hidden })}
              </Typo>
              <Ionicons name="chevron-forward" size={14} color={colors.textSecondary} />
            </View>
          </Pressable>
          <View style={styles.divider} />
          <BreakdownRow label="Disponible" value={committed.available} hidden={hidden} strong />
        </View>
      ) : null}

      {committed.isOvercommitted ? (
        <Pressable onPress={onOpenCommitted} accessibilityRole="alert" style={styles.alert}>
          <Ionicons name="warning-outline" size={16} color={colors.danger} />
          <Typo variant="caption" color={colors.text} style={styles.flex}>
            {hidden
              ? 'Tienes más comprometido de lo que tienes disponible.'
              : `Tienes ${formatCurrency(committed.overcommitted)} más comprometidos de lo que tienes disponible.`}
          </Typo>
        </Pressable>
      ) : null}
    </View>
  );
}

function BreakdownRow({
  label,
  value,
  hidden,
  strong,
}: {
  label: string;
  value: number;
  hidden: boolean;
  strong?: boolean;
}) {
  return (
    <View style={styles.row}>
      <Typo variant="caption" color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular color={colors.text}>
        {formatCurrency(value, { hidden })}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  breakdown: {
    marginTop: spacing.md,
    gap: spacing.xs,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  pressableRow: {
    minHeight: 32,
  },
  pressed: {
    opacity: 0.6,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.xs,
  },
  alert: {
    marginTop: spacing.md,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: 'rgba(216, 102, 91, 0.10)',
    borderRadius: radius.md,
    padding: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
