import { useEffect, useState } from 'react';
import { Animated, StyleSheet, View, type ViewStyle } from 'react-native';
import { colors, radius, spacing } from '../../theme';

interface SkeletonProps {
  height?: number;
  width?: number | `${number}%`;
  style?: ViewStyle;
}

/** A calm pulse, not a shimmer: the app should feel unhurried even while loading. */
export function Skeleton({ height = 16, width = '100%', style }: SkeletonProps) {
  const [opacity] = useState(() => new Animated.Value(0.5));

  useEffect(() => {
    const animation = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 1, duration: 700, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.5, duration: 700, useNativeDriver: true }),
      ]),
    );

    animation.start();
    return () => animation.stop();
  }, [opacity]);

  return <Animated.View style={[styles.block, { height, width, opacity }, style]} />;
}

export function SkeletonCard() {
  return (
    <View style={styles.card}>
      <Skeleton height={14} width="40%" />
      <Skeleton height={34} width="65%" />
      <Skeleton height={14} width="30%" />
    </View>
  );
}

const styles = StyleSheet.create({
  block: {
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.sm,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
});
