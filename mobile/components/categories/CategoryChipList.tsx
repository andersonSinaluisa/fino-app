import { ActivityIndicator, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui';
import { iconForCategory } from '../../utils/categoryIcons';
import type { Category } from '../../types/api';

interface CategoryChipListProps {
  categories: Category[];
  selectedId: string | null | undefined;
  onSelect: (category: Category) => void;
  onCreateNew: () => void;
  onEdit?: (category: Category) => void;
  disabled?: boolean;
  loading?: boolean;
}

/**
 * Categorías personalizadas: un único picker de categorías en chips,
 * reutilizado por "Cambiar categoría" (movimiento/[id].tsx) y por el
 * selector de categoría destino de una regla (reglas-categorizacion) --
 * antes cada pantalla tenía su propia fila de chips casi idéntica. Agrega
 * el ícono/color de cada categoría (antes solo texto) y un chip final
 * "Nueva" para crear una categoría propia sin salir del flujo. El lápiz
 * solo aparece en categorías propias (`!isSystem`) -- las del sistema no
 * se pueden editar ni aquí ni en el backend.
 */
export function CategoryChipList({
  categories,
  selectedId,
  onSelect,
  onCreateNew,
  onEdit,
  disabled,
  loading,
}: CategoryChipListProps) {
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.list}>
      {loading ? <ActivityIndicator color={colors.textSecondary} style={styles.loading} /> : null}

      {categories.map((category) => {
        const selected = category.id === selectedId;
        return (
          <View key={category.id} style={styles.chipGroup}>
            <Pressable
              disabled={disabled}
              onPress={() => onSelect(category)}
              style={[styles.chip, selected ? styles.chipSelected : null]}
            >
              <Ionicons
                name={iconForCategory(category.icon)}
                size={14}
                color={selected ? colors.onPrimary : category.color}
              />
              <Typo variant="caption" color={selected ? colors.onPrimary : colors.text}>
                {category.name}
              </Typo>
            </Pressable>

            {!category.isSystem && onEdit ? (
              <Pressable
                disabled={disabled}
                onPress={() => onEdit(category)}
                hitSlop={8}
                accessibilityRole="button"
                accessibilityLabel={`Editar ${category.name}`}
                style={styles.editButton}
              >
                <Ionicons name="pencil-outline" size={12} color={colors.textSecondary} />
              </Pressable>
            ) : null}
          </View>
        );
      })}

      <Pressable
        disabled={disabled}
        onPress={onCreateNew}
        style={[styles.chip, styles.chipNew]}
        accessibilityRole="button"
        accessibilityLabel="Nueva categoría"
      >
        <Ionicons name="add" size={14} color={colors.textSecondary} />
        <Typo variant="caption" color={colors.textSecondary}>
          Nueva
        </Typo>
      </Pressable>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  list: {
    gap: spacing.sm,
    paddingRight: spacing.lg,
    alignItems: 'center',
  },
  loading: {
    marginRight: spacing.sm,
  },
  chipGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm + 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  chipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  chipNew: {
    borderStyle: 'dashed',
  },
  editButton: {
    width: 22,
    height: 22,
    borderRadius: 11,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
});
