import { Pressable, StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';

interface AuthFooterProps {
  question: string;
  actionLabel: string;
  onPress: () => void;
}

/** "¿Nuevo en Fino? Crear cuenta" -- y cualquier otro par pregunta/acción de auth. */
export function AuthFooter({ question, actionLabel, onPress }: AuthFooterProps) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="link"
      accessibilityLabel={actionLabel}
      hitSlop={8}
      style={styles.wrapper}
    >
      <View style={styles.row}>
        <Typo variant="caption" color={colors.textSecondary}>
          {question}{' '}
        </Typo>
        <Typo variant="caption" color={colors.text} style={styles.action}>
          {actionLabel}
        </Typo>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    marginTop: spacing.xxl,
    minHeight: 44,
    alignItems: 'center',
    justifyContent: 'center',
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  action: {
    fontWeight: '600',
  },
});
