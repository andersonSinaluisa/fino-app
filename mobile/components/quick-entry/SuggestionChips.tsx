import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import { iconForCategory } from '../../utils/categoryIcons';
import type { QuickEntrySuggestion } from '../../types/api';

/**
 * §17 ("frecuentes") y §19 ("recientes").
 *
 * Un toque = un movimiento registrado (§17: "no pedir confirmación"), pero solo
 * cuando el monto típico es confiable. Cuando no lo es, tocar el chip rellena la
 * etiqueta y la categoría y deja el monto en blanco para que la persona lo escriba:
 * proponer un número inventado en una app de dinero es peor que pedir un toque más.
 *
 * §18: mantener pulsado abre el mini editor.
 */

interface SuggestionChipsProps {
  title: string;
  suggestions: readonly QuickEntrySuggestion[];
  onUse: (suggestion: QuickEntrySuggestion) => void;
  onEdit: (suggestion: QuickEntrySuggestion) => void;
  disabled?: boolean;
}

export function SuggestionChips({
  title,
  suggestions,
  onUse,
  onEdit,
  disabled = false,
}: SuggestionChipsProps) {
  if (suggestions.length === 0) {
    return null;
  }

  return (
    <View style={styles.container}>
      <Typo variant="overline" color={colors.textSecondary}>
        {title.toUpperCase()}
      </Typo>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.row}
        keyboardShouldPersistTaps="handled"
      >
        {suggestions.map((suggestion) => {
          const isIncome = suggestion.direction === 'Income';

          return (
            <Pressable
              key={`${suggestion.label}-${suggestion.direction}`}
              testID={`suggestion-${suggestion.label}`}
              accessibilityRole="button"
              // §37: la etiqueta accesible dice lo que va a pasar, no solo lo que
              // pone el chip -- quien usa TalkBack no ve que el monto está en gris.
              accessibilityLabel={
                suggestion.amountIsReliable
                  ? `Registrar ${suggestion.label} por ${formatCurrency(suggestion.typicalAmount)}`
                  : `Usar ${suggestion.label}, escribir el monto`
              }
              accessibilityHint="Mantén pulsado para editar"
              disabled={disabled}
              onPress={() => {
                void Haptics.selectionAsync().catch(() => undefined);
                onUse(suggestion);
              }}
              onLongPress={() => {
                void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium).catch(() => undefined);
                onEdit(suggestion);
              }}
              delayLongPress={350}
              style={({ pressed }) => [
                styles.chip,
                pressed && !disabled ? styles.chipPressed : null,
                disabled ? styles.chipDisabled : null,
              ]}
            >
              <View
                style={[
                  styles.icon,
                  { backgroundColor: suggestion.categoryColor ?? colors.surfaceSecondary },
                ]}
              >
                <Ionicons
                  name={iconForCategory(suggestion.categoryIcon)}
                  size={14}
                  color={colors.primary}
                />
              </View>

              <Typo variant="body" numberOfLines={1} style={styles.label}>
                {suggestion.label}
              </Typo>

              {suggestion.amountIsReliable ? (
                <Typo
                  variant="bodyStrong"
                  tabular
                  color={isIncome ? colors.success : colors.text}
                >
                  {formatCurrency(suggestion.typicalAmount)}
                </Typo>
              ) : null}
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingRight: spacing.lg,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    // §37: 44px de alto mínimo, también en un chip.
    minHeight: 44,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    maxWidth: 210,
  },
  chipPressed: {
    backgroundColor: colors.surfaceSecondary,
  },
  chipDisabled: {
    opacity: 0.5,
  },
  icon: {
    width: 24,
    height: 24,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  label: {
    flexShrink: 1,
  },
});
