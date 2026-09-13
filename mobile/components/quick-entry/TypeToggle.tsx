import { Pressable, StyleSheet, View } from 'react-native';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import type { TransactionDirection } from '../../types/api';

/**
 * §5 ("gasto / ingreso"): "selector muy simple, no utilizar tabs enormes, cambio
 * inmediato".
 *
 * La decisión de color está en el §5 y es deliberadamente contraria a lo que hace
 * casi toda app de finanzas: el gasto NO se pinta de rojo intenso. La gente gasta
 * dinero todos los días; teñir de rojo cada almuerzo convierte el uso normal de la
 * app en una regañina. El gasto es neutro (tinta) y el ingreso es el verde de Fino.
 */

interface TypeToggleProps {
  value: TransactionDirection;
  onChange: (direction: TransactionDirection) => void;
  disabled?: boolean;
}

const OPTIONS: ReadonlyArray<{
  direction: TransactionDirection;
  label: string;
  sign: string;
  accessibilityLabel: string;
}> = [
  { direction: 'Expense', label: 'Gasto', sign: '−', accessibilityLabel: 'Gasto' },
  { direction: 'Income', label: 'Ingreso', sign: '+', accessibilityLabel: 'Ingreso' },
];

export function TypeToggle({ value, onChange, disabled = false }: TypeToggleProps) {
  return (
    <View style={styles.track} accessibilityRole="radiogroup">
      {OPTIONS.map((option) => {
        const selected = value === option.direction;
        const isIncome = option.direction === 'Income';

        return (
          <Pressable
            key={option.direction}
            testID={`type-toggle-${option.direction}`}
            accessibilityRole="radio"
            accessibilityState={{ selected, disabled }}
            accessibilityLabel={option.accessibilityLabel}
            disabled={disabled}
            onPress={() => {
              if (selected) {
                return;
              }

              void Haptics.selectionAsync().catch(() => undefined);
              onChange(option.direction);
            }}
            style={({ pressed }) => [
              styles.option,
              selected ? (isIncome ? styles.selectedIncome : styles.selectedExpense) : null,
              pressed && !selected ? styles.pressed : null,
            ]}
          >
            {/* §37: "no depender solo del color." El signo dice lo mismo que el
                fondo, así que el selector se entiende en escala de grises y con
                daltonismo. */}
            <Typo
              variant="bodyStrong"
              color={selected ? (isIncome ? colors.onAccent : colors.onPrimary) : colors.textSecondary}
            >
              {option.sign} {option.label}
            </Typo>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  track: {
    flexDirection: 'row',
    gap: spacing.xs,
    padding: spacing.xs,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
  },
  option: {
    flex: 1,
    minHeight: 44,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.pill,
  },
  selectedExpense: {
    backgroundColor: colors.primary,
  },
  selectedIncome: {
    backgroundColor: colors.accentSecondary,
  },
  pressed: {
    opacity: 0.6,
  },
});
