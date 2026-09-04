import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View, type ViewStyle, RefreshControl } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, spacing } from '../../theme';

interface ScreenProps {
  children: ReactNode;
  scroll?: boolean;
  padded?: boolean;
  refreshing?: boolean;
  onRefresh?: () => void;
  contentStyle?: ViewStyle;
  /** Set when a screen renders its own header and should not inset the top. */
  edgeToEdgeTop?: boolean;
}

export function Screen({
  children,
  scroll = true,
  padded = true,
  refreshing,
  onRefresh,
  contentStyle,
  edgeToEdgeTop = false,
}: ScreenProps) {
  const insets = useSafeAreaInsets();

  const padding: ViewStyle = {
    paddingTop: edgeToEdgeTop ? 0 : insets.top + spacing.sm,
    paddingBottom: insets.bottom + spacing.xxl,
    paddingHorizontal: padded ? spacing.xl : 0,
  };

  if (!scroll) {
    return <View style={[styles.root, padding, contentStyle]}>{children}</View>;
  }

  return (
    <ScrollView
      style={styles.root}
      contentContainerStyle={[padding, contentStyle]}
      showsVerticalScrollIndicator={false}
      keyboardShouldPersistTaps="handled"
      refreshControl={
        onRefresh
          ? <RefreshControl refreshing={!!refreshing} onRefresh={onRefresh} tintColor={colors.textSecondary} />
          : undefined
      }
    >
      {children}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
});
