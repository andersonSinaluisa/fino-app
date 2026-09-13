import { useEffect, useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Typo } from '../ui/Typo';
import { Button } from '../ui/Button';
import { iconForCategory } from '../../utils/categoryIcons';
import type { Category, QuickEntrySuggestion } from '../../types/api';

/**
 * §18: "mantener presionado un frecuente abre un mini editor."
 *
 * Y la segunda mitad del §18, que es la que decide el tamaño de esto: "no convertir
 * frecuentes en una sección administrativa compleja". Por eso solo hay monto y
 * categoría, y el resultado es guardar el movimiento con esos valores -- no editar
 * una entidad "frecuente" que no existe. Los frecuentes se derivan del historial,
 * así que la forma de cambiar uno es registrar el siguiente como toca.
 */

interface SuggestionEditorSheetProps {
  suggestion: QuickEntrySuggestion | null;
  categories: readonly Category[];
  onClose: () => void;
  onSave: (amount: number, categoryId: string | null) => void;
}

export function SuggestionEditorSheet({
  suggestion,
  categories,
  onClose,
  onSave,
}: SuggestionEditorSheetProps) {
  const [amountText, setAmountText] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);

  useEffect(() => {
    if (!suggestion) {
      return;
    }

    setAmountText(suggestion.amountIsReliable ? suggestion.typicalAmount.toFixed(2) : '');
    setCategoryId(suggestion.categoryId);
  }, [suggestion]);

  const amount = Number(amountText.replace(',', '.'));
  const valid = Number.isFinite(amount) && amount > 0;

  return (
    <Modal visible={suggestion !== null} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable style={styles.backdrop} onPress={onClose} accessibilityRole="button" accessibilityLabel="Cerrar" />

      <View style={styles.sheet}>
        <View style={styles.handle} />

        <Typo variant="heading">{suggestion?.label ?? ''}</Typo>

        <View style={styles.field}>
          <Typo variant="caption" color={colors.textSecondary}>
            Monto
          </Typo>
          <View style={styles.amountRow}>
            <Typo style={styles.currency}>$</Typo>
            <TextInput
              value={amountText}
              onChangeText={setAmountText}
              keyboardType="decimal-pad"
              style={styles.amountInput}
              placeholder="0.00"
              placeholderTextColor={colors.textSecondary}
              accessibilityLabel="Monto"
              autoFocus
            />
          </View>
        </View>

        <View style={styles.field}>
          <Typo variant="caption" color={colors.textSecondary}>
            Categoría
          </Typo>

          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.categories}>
            <Pressable
              accessibilityRole="button"
              accessibilityState={{ selected: categoryId === null }}
              onPress={() => setCategoryId(null)}
              style={[styles.category, categoryId === null ? styles.categorySelected : null]}
            >
              <Typo variant="caption" color={categoryId === null ? colors.onPrimary : colors.text}>
                Sin categoría
              </Typo>
            </Pressable>

            {categories.map((category) => {
              const selected = category.id === categoryId;

              return (
                <Pressable
                  key={category.id}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  accessibilityLabel={category.name}
                  onPress={() => setCategoryId(category.id)}
                  style={[styles.category, selected ? styles.categorySelected : null]}
                >
                  <Ionicons
                    name={iconForCategory(category.icon)}
                    size={14}
                    color={selected ? colors.onPrimary : colors.textSecondary}
                  />
                  <Typo variant="caption" color={selected ? colors.onPrimary : colors.text}>
                    {category.name}
                  </Typo>
                </Pressable>
              );
            })}
          </ScrollView>
        </View>

        <Button
          label="Guardar cambio"
          onPress={() => onSave(Number(amountText.replace(',', '.')), categoryId)}
          disabled={!valid}
          softDisabled
        />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: colors.overlay,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.xxl,
    gap: spacing.lg,
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
  },
  field: {
    gap: spacing.sm,
  },
  amountRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
  },
  currency: {
    fontSize: typography.heading.fontSize,
    color: colors.textSecondary,
  },
  amountInput: {
    flex: 1,
    minHeight: 56,
    color: colors.text,
    fontSize: typography.heading.fontSize,
    fontWeight: '700',
  },
  categories: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingRight: spacing.lg,
  },
  category: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    minHeight: 44,
    paddingHorizontal: spacing.md,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
  },
  categorySelected: {
    backgroundColor: colors.primary,
  },
});
