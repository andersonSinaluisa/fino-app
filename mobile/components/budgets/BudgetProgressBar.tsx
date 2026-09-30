import { StyleSheet, View } from 'react-native';
import { colors, radius } from '../../theme';
import type { BudgetProgress, BudgetUsageLevel } from '../../types/api';
import { progressFill } from '../../utils/budgets';

/**
 * La barra refuerza el estado, nunca lo comunica sola: el texto de al lado
 * (porcentaje + "Te quedan…"/"Excediste…") dice lo mismo sin depender del
 * color. Los tonos son los de FINO -- tinta para lo normal, ámbar para
 * atención y el rojo suave de la marca solo al exceder (nada de rojo intenso).
 */
const FILL: Record<BudgetUsageLevel, string> = {
  Normal: colors.primary,
  Attention: colors.warning,
  NearLimit: colors.warning,
  Exceeded: colors.danger,
};

interface BudgetProgressBarProps {
  progress: BudgetProgress;
  /** Un presupuesto pausado se ve atenuado. */
  muted?: boolean;
  height?: number;
}

export function BudgetProgressBar({ progress, muted = false, height = 8 }: BudgetProgressBarProps) {
  const fill = progressFill(progress);

  return (
    <View
      accessibilityRole="progressbar"
      accessibilityValue={{ min: 0, max: 100, now: Math.round(Math.min(progress.percentUsed, 100)) }}
      style={[styles.track, { height, borderRadius: height / 2 }]}
    >
      <View
        style={[
          styles.fill,
          {
            width: `${fill}%`,
            backgroundColor: muted ? colors.textSecondary : FILL[progress.level],
            borderRadius: height / 2,
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  track: {
    width: '100%',
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
    borderRadius: radius.pill,
  },
  fill: {
    height: '100%',
  },
});
