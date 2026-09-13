import { Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from './Typo';

export interface SelectSheetOption<T extends string | undefined> {
  value: T;
  label: string;
}

interface SelectSheetProps<T extends string | undefined> {
  visible: boolean;
  title: string;
  options: SelectSheetOption<T>[];
  selectedValue: T;
  onSelect: (value: T) => void;
  onClose: () => void;
}

/**
 * Estadísticas (rediseño 2026-09): reemplaza los carruseles horizontales de
 * chips por un selector único que abre esto -- un bottom sheet genérico,
 * reutilizable para "Periodo" y "Cuenta" (y para cualquier otro filtro futuro
 * de una sola selección), sin traer una librería de bottom sheets nueva.
 */
export function SelectSheet<T extends string | undefined>({
  visible,
  title,
  options,
  selectedValue,
  onSelect,
  onClose,
}: SelectSheetProps<T>) {
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
          {title}
        </Typo>

        <ScrollView contentContainerStyle={styles.listContent} showsVerticalScrollIndicator={false}>
          {options.map((option) => {
            const selected = option.value === selectedValue;
            return (
              <Pressable
                key={option.value ?? '__all__'}
                accessibilityRole="button"
                accessibilityState={{ selected }}
                onPress={() => onSelect(option.value)}
                style={({ pressed }) => [styles.row, pressed ? styles.rowPressed : null]}
              >
                <Typo
                  variant="body"
                  color={selected ? colors.text : colors.textSecondary}
                  numberOfLines={1}
                  style={styles.flex}
                >
                  {option.label}
                </Typo>
                {selected ? <Ionicons name="checkmark" size={18} color={colors.text} /> : null}
              </Pressable>
            );
          })}
        </ScrollView>
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
    maxHeight: '70%',
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
    marginBottom: spacing.sm,
  },
  listContent: {
    paddingBottom: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    paddingVertical: spacing.md,
  },
  rowPressed: {
    opacity: 0.6,
  },
  flex: {
    flex: 1,
  },
});
