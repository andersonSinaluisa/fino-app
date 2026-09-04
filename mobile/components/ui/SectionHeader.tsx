import { Pressable, StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from './Typo';

interface SectionHeaderProps {
  title: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function SectionHeader({ title, actionLabel, onAction }: SectionHeaderProps) {
  return (
    <View style={styles.row}>
      <Typo variant="overline" color={colors.textSecondary}>
        {title.toUpperCase()}
      </Typo>

      {actionLabel && onAction ? (
        <Pressable onPress={onAction} hitSlop={8}>
          <Typo variant="caption" color={colors.text}>
            {actionLabel}
          </Typo>
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.md,
  },
});
