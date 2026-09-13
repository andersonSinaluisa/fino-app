import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import { estimateAvailableMoney } from '../../utils/planCalculations';
import type { HomeSummary } from '../../types/api';

interface AvailableMoneySectionProps {
  summary: HomeSummary;
  hidden: boolean;
}

/**
 * Rediseño de Home (2026-09): el concepto que de verdad importa en Fino no es
 * "cuánto hay en el banco" (eso ya lo dice BalanceHeader) sino "cuánto puedo
 * gastar de verdad" -- el mismo cálculo que ya usa la pantalla
 * app/dinero-disponible (estimateAvailableMoney), reutilizado aquí sin
 * duplicar su lógica. El breakdown "Tu dinero / Comprometido / Disponible"
 * solo se muestra cuando hay gasto proyectado real que comprometa parte del
 * dinero -- si no, mostrarlo sería inventar una cifra de "$0 comprometidos"
 * que no aporta nada.
 */
export function AvailableMoneySection({ summary, hidden }: AvailableMoneySectionProps) {
  if (summary.accountCount === 0) {
    return null;
  }

  const estimate = estimateAvailableMoney(summary);
  const hasCommitted = estimate.projectedRemainingExpense >= 1;

  return (
    <View style={styles.wrapper}>
      <Typo variant="caption" color={colors.textSecondary}>
        Disponible ahora
      </Typo>
      <Typo variant="title" tabular>
        {hidden ? '••••••' : formatCurrency(estimate.availableUntilMonthEnd)}
      </Typo>

      {hasCommitted ? (
        <View style={styles.breakdown}>
          <BreakdownRow label="Tu dinero" value={summary.totalBalance} hidden={hidden} />
          <BreakdownRow label="Comprometido" value={estimate.projectedRemainingExpense} hidden={hidden} muted />
          <View style={styles.divider} />
          <BreakdownRow label="Disponible" value={estimate.availableUntilMonthEnd} hidden={hidden} strong />
        </View>
      ) : null}
    </View>
  );
}

function BreakdownRow({
  label,
  value,
  hidden,
  strong,
  muted,
}: {
  label: string;
  value: number;
  hidden: boolean;
  strong?: boolean;
  muted?: boolean;
}) {
  return (
    <View style={styles.row}>
      <Typo variant="caption" color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo
        variant={strong ? 'bodyStrong' : 'body'}
        tabular
        color={muted ? colors.textSecondary : colors.text}
      >
        {hidden ? '••••' : formatCurrency(value)}
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
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.xs,
  },
});
