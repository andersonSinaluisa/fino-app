import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View, type ViewStyle } from 'react-native';
import { colors, elevation, radius, spacing } from '../../theme';

interface CardProps {
  children: ReactNode;
  onPress?: () => void;
  style?: ViewStyle;
  tone?: 'surface' | 'secondary' | 'ink' | 'accent';
  padded?: boolean;
}

const toneBackground: Record<NonNullable<CardProps['tone']>, string> = {
  surface: colors.surface,
  secondary: colors.surfaceSecondary,
  ink: colors.primary,
  accent: colors.accent,
};

export function Card({ children, onPress, style, tone = 'surface', padded = true }: CardProps) {
  const content = (
    <View
      style={[
        styles.card,
        { backgroundColor: toneBackground[tone] },
        padded ? styles.padded : null,
        tone === 'surface' ? elevation.card : null,
        style,
      ]}
    >
      {children}
    </View>
  );

  if (!onPress) {
    return content;
  }

  return (
    <Pressable onPress={onPress} style={({ pressed }) => (pressed ? styles.pressed : undefined)}>
      {content}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.lg,
    overflow: 'hidden',
  },
  padded: {
    padding: spacing.lg,
  },
  pressed: {
    // A small, quick scale reads as "responsive" without being playful.
    transform: [{ scale: 0.985 }],
    opacity: 0.95,
  },
});
