import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Typo } from './Typo';
import { Button } from './Button';

interface EmptyStateProps {
  icon?: keyof typeof Ionicons.glyphMap;
  title: string;
  body?: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ icon = 'sparkles-outline', title, body, actionLabel, onAction }: EmptyStateProps) {
  return (
    <View style={styles.wrapper}>
      <View style={styles.iconWrap}>
        <Ionicons name={icon} size={26} color={colors.textSecondary} />
      </View>

      <Typo variant="heading" align="center">
        {title}
      </Typo>

      {body ? (
        <Typo variant="body" color={colors.textSecondary} align="center">
          {body}
        </Typo>
      ) : null}

      {actionLabel && onAction ? (
        <View style={styles.action}>
          <Button label={actionLabel} onPress={onAction} variant="primary" fullWidth={false} compact />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.xxxl,
    paddingHorizontal: spacing.xl,
  },
  iconWrap: {
    width: 56,
    height: 56,
    borderRadius: 20,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
  action: {
    marginTop: spacing.sm,
  },
});
