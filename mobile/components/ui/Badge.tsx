import { StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Typo } from './Typo';

type Tone = 'neutral' | 'positive' | 'attention' | 'danger' | 'accent';

interface BadgeProps {
  label: string;
  tone?: Tone;
}

const palette: Record<Tone, { background: string; text: string }> = {
  neutral: { background: colors.surfaceSecondary, text: colors.textSecondary },
  positive: { background: 'rgba(78, 159, 115, 0.14)', text: colors.success },
  attention: { background: 'rgba(228, 168, 83, 0.16)', text: '#9A6A16' },
  danger: { background: 'rgba(216, 102, 91, 0.14)', text: colors.danger },
  accent: { background: colors.accent, text: colors.onAccent },
};

export function Badge({ label, tone = 'neutral' }: BadgeProps) {
  const { background, text } = palette[tone];

  return (
    <View style={[styles.badge, { backgroundColor: background }]}>
      <Typo variant="overline" color={text}>
        {label.toUpperCase()}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    alignSelf: 'flex-start',
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: 5,
  },
});
