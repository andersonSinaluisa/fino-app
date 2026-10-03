import { useEffect, useState } from 'react';
import { Modal, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Button, KeyboardAwareScrollView, KeyboardSpacer, Typo } from '../ui';
import { CATEGORY_COLORS, CATEGORY_ICON_KEYS, iconForCategory } from '../../utils/categoryIcons';
import type { Category } from '../../types/api';

interface CategoryFormSheetProps {
  visible: boolean;
  /** Presente => modo edición (prellena nombre/ícono/color); ausente => crear. */
  category?: Category | null;
  onClose: () => void;
  onSubmit: (input: { name: string; icon: string; color: string }) => void;
  submitting?: boolean;
  errorMessage?: string | null;
}

const DEFAULT_ICON = 'circle';
const DEFAULT_COLOR = '#8DD9B6';

/**
 * Categorías personalizadas: "crear categoría" y "editar categoría" son la
 * misma hoja -- mismos campos, misma validación visual, solo cambia si
 * category llega prellenada. El ícono/color disponibles salen de
 * utils/categoryIcons.ts (CATEGORY_ICON_KEYS/CATEGORY_COLORS), la misma
 * lista que el backend valida (Nexo.Domain.Categories.CategoryAppearance),
 * así que nunca se puede enviar una combinación que el servidor rechace.
 */
export function CategoryFormSheet({
  visible,
  category,
  onClose,
  onSubmit,
  submitting,
  errorMessage,
}: CategoryFormSheetProps) {
  const [name, setName] = useState('');
  const [icon, setIcon] = useState(DEFAULT_ICON);
  const [color, setColor] = useState(DEFAULT_COLOR);

  useEffect(() => {
    if (visible) {
      setName(category?.name ?? '');
      setIcon(category?.icon ?? DEFAULT_ICON);
      setColor(category?.color ?? DEFAULT_COLOR);
    }
  }, [visible, category]);

  const canSubmit = name.trim().length > 0 && !submitting;

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable
        style={styles.backdrop}
        onPress={onClose}
        accessibilityRole="button"
        accessibilityLabel="Cerrar"
      />

      <View style={styles.sheet}>
        <View style={styles.handle} />
        <Typo variant="bodyStrong" style={styles.title}>
          {category ? 'Editar categoría' : 'Nueva categoría'}
        </Typo>

        <KeyboardAwareScrollView
          keyboardSpacer={false}
          showsVerticalScrollIndicator={false}
          contentContainerStyle={styles.body}
          keyboardShouldPersistTaps="handled"
        >
          <TextInput
            value={name}
            onChangeText={setName}
            placeholder="Nombre, por ejemplo: Mascotas"
            placeholderTextColor={colors.textSecondary}
            style={styles.input}
            maxLength={60}
            autoFocus
          />

          <Typo variant="caption" color={colors.textSecondary} style={styles.sectionLabel}>
            Ícono
          </Typo>
          <View style={styles.grid}>
            {CATEGORY_ICON_KEYS.map((key) => {
              const selected = key === icon;
              return (
                <Pressable
                  key={key}
                  onPress={() => setIcon(key)}
                  accessibilityRole="button"
                  accessibilityLabel={`Ícono ${key}`}
                  accessibilityState={{ selected }}
                  style={[styles.iconSwatch, selected ? { backgroundColor: color, borderColor: color } : null]}
                >
                  <Ionicons
                    name={iconForCategory(key)}
                    size={18}
                    color={selected ? colors.onPrimary : colors.textSecondary}
                  />
                </Pressable>
              );
            })}
          </View>

          <Typo variant="caption" color={colors.textSecondary} style={styles.sectionLabel}>
            Color
          </Typo>
          <View style={styles.grid}>
            {CATEGORY_COLORS.map((hex) => {
              const selected = hex === color;
              return (
                <Pressable
                  key={hex}
                  onPress={() => setColor(hex)}
                  accessibilityRole="button"
                  accessibilityLabel={`Color ${hex}`}
                  accessibilityState={{ selected }}
                  style={[styles.colorSwatch, { backgroundColor: hex }, selected ? styles.colorSwatchSelected : null]}
                >
                  {selected ? <Ionicons name="checkmark" size={16} color={colors.onPrimary} /> : null}
                </Pressable>
              );
            })}
          </View>

          {errorMessage ? (
            <Typo variant="caption" color={colors.danger} style={styles.error}>
              {errorMessage}
            </Typo>
          ) : null}
        </KeyboardAwareScrollView>

        <View style={styles.submitRow}>
          <Button
            label={category ? 'Guardar cambios' : 'Crear categoría'}
            onPress={() => onSubmit({ name: name.trim(), icon, color })}
            disabled={!canSubmit}
            loading={submitting}
            loadingLabel="Guardando..."
          />
        </View>
      </View>
      <KeyboardSpacer />
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
    maxHeight: '82%',
    flexShrink: 1,
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.md,
  },
  title: {
    marginBottom: spacing.md,
  },
  body: {
    paddingBottom: spacing.md,
  },
  input: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    color: colors.text,
    fontSize: typography.body.fontSize,
    marginBottom: spacing.lg,
  },
  sectionLabel: {
    marginBottom: spacing.sm,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginBottom: spacing.lg,
  },
  iconSwatch: {
    width: 42,
    height: 42,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  colorSwatch: {
    width: 32,
    height: 32,
    borderRadius: 16,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 2,
    borderColor: 'transparent',
  },
  colorSwatchSelected: {
    borderColor: colors.text,
  },
  error: {
    marginBottom: spacing.sm,
  },
  submitRow: {
    marginTop: spacing.xs,
  },
});
