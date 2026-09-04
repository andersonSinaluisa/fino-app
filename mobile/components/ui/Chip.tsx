import { Pressable, StyleSheet } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from './Typo';

interface ChipProps {
  label: string;
  selected?: boolean;
  onPress: () => void;
}

/** Filter pill used across the movements screen. */
export function Chip({ label, selected = false, onPress }: ChipProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={({ pressed }) => [
        styles.chip,
        selected ? styles.selected : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <Typo variant="caption" color={selected ? colors.onPrimary : colors.textSecondary}>
        {label}
      </Typo>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm + 1,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  selected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  pressed: {
    opacity: 0.85,
  },
});
