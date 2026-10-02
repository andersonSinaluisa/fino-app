import { Pressable, StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Badge, Typo } from '../ui';
import { formatCurrency } from '../../utils/format';
import { installmentProgressLabel, nextInstallmentLabel } from '../../utils/creditCards';
import type { InstallmentPlan } from '../../types/api';

interface InstallmentPlanRowProps {
  plan: InstallmentPlan;
  hidden: boolean;
  onPress?: (plan: InstallmentPlan) => void;
}

/**
 * "Laptop · $1,200 · 4/12 · Próxima $100 · Pendiente $800". El monto original
 * es lo que se GASTÓ (una vez, el día de la compra); "pendiente" es deuda
 * futura, no dinero comprometido hoy.
 */
export function InstallmentPlanRow({ plan, hidden, onPress }: InstallmentPlanRowProps) {
  const money = (value: number) => formatCurrency(value, { hidden });

  return (
    <Pressable
      onPress={onPress ? () => onPress(plan) : undefined}
      disabled={!onPress}
      accessibilityRole={onPress ? 'button' : undefined}
      accessibilityLabel={`${plan.description}, cuota ${installmentProgressLabel(plan)}`}
      style={({ pressed }) => [styles.row, pressed && onPress ? styles.pressed : null]}
    >
      <View style={styles.body}>
        <View style={styles.titleRow}>
          <Typo variant="bodyStrong" numberOfLines={1} style={styles.flex}>
            {plan.description}
          </Typo>
          <Typo variant="bodyStrong" tabular>
            {installmentProgressLabel(plan)}
          </Typo>
        </View>
        <Typo variant="caption" color={colors.textSecondary}>
          {money(plan.originalAmount)} en {plan.numberOfInstallments} cuotas{plan.categoryName ? ` · ${plan.categoryName}` : ''}
        </Typo>
        <View style={styles.titleRow}>
          <Typo variant="caption" color={colors.text}>
            {nextInstallmentLabel(plan, hidden)}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary} tabular>
            Pendiente {money(plan.outstandingAmount)}
          </Typo>
        </View>
        {plan.status !== 'Active' ? (
          <Badge label={plan.status === 'Cancelled' ? 'Precancelado' : 'Completado'} tone={plan.status === 'Cancelled' ? 'neutral' : 'positive'} />
        ) : null}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    paddingVertical: spacing.md,
  },
  pressed: {
    opacity: 0.6,
  },
  body: {
    gap: 4,
  },
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
