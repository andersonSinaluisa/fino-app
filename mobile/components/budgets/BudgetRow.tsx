import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Typo } from '../ui';
import { BudgetProgressBar } from './BudgetProgressBar';
import { iconForCategory } from '../../utils/categoryIcons';
import { formatCurrency } from '../../utils/format';
import { LEVEL_PRESENTATION, budgetAccessibilityLabel, budgetStatusMessage } from '../../utils/budgets';
import type { Budget } from '../../types/api';

interface BudgetRowProps {
  budget: Budget;
  hidden: boolean;
  onPress: () => void;
}

/**
 * Una fila de la lista de Presupuestos: "¿cuánto planeé y cómo voy?" de un
 * vistazo -- ícono de la categoría (el mismo de Movimientos), "$120 de $300",
 * barra, porcentaje y el mensaje de estado.
 */
export function BudgetRow({ budget, hidden, onPress }: BudgetRowProps) {
  const progress = budget.progress;
  const tint = budget.categoryColor ?? colors.surfaceSecondary;

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={progress ? budgetAccessibilityLabel(budget.name, progress, hidden) : budget.name}
      accessibilityHint="Abre el detalle del presupuesto"
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
    >
      <View style={styles.header}>
        <View style={[styles.icon, { backgroundColor: tint }]}>
          <Ionicons
            name={budget.categoryIcon ? iconForCategory(budget.categoryIcon) : 'pie-chart-outline'}
            size={18}
            color={colors.primary}
          />
        </View>

        <View style={styles.flex}>
          <Typo variant="bodyStrong" numberOfLines={1}>
            {budget.name}
          </Typo>
          {progress ? (
            <Typo variant="caption" color={colors.textSecondary} tabular>
              {hidden ? '••••' : `${formatCurrency(progress.spent)} de ${formatCurrency(progress.amount)}`}
            </Typo>
          ) : null}
        </View>

        <View style={styles.badges}>
          {!budget.isActive ? <Badge label="Pausado" tone="neutral" /> : null}
          {budget.isActive && budget.reserveFunds ? <Badge label="Reservado" tone="accent" /> : null}
        </View>
      </View>

      {progress ? (
        <>
          <BudgetProgressBar progress={progress} muted={!budget.isActive} />
          <View style={styles.footer}>
            <View style={styles.status}>
              <Ionicons
                name={LEVEL_PRESENTATION[progress.level].icon}
                size={14}
                color={progress.level === 'Exceeded' ? colors.danger : colors.textSecondary}
              />
              <Typo variant="caption" color={progress.level === 'Exceeded' ? colors.danger : colors.textSecondary}>
                {budgetStatusMessage(progress, hidden)}
              </Typo>
            </View>
            <Typo variant="caption" color={colors.text} tabular>
              {Math.floor(progress.percentUsed)}%
            </Typo>
          </View>
        </>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
  pressed: {
    opacity: 0.85,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  icon: {
    width: 38,
    height: 38,
    borderRadius: 19,
    alignItems: 'center',
    justifyContent: 'center',
  },
  flex: {
    flex: 1,
  },
  badges: {
    flexDirection: 'row',
    gap: spacing.xs,
  },
  footer: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: spacing.sm,
  },
  status: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    flexShrink: 1,
  },
});
