import type { ReactNode } from 'react';
import {
  Keyboard,
  StyleSheet,
  TouchableWithoutFeedback,
  View,
  type ViewStyle,
  RefreshControl,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, spacing } from '../../theme';
import { KeyboardAwareScrollView } from './KeyboardAwareScrollView';

interface ScreenProps {
  children: ReactNode;
  scroll?: boolean;
  padded?: boolean;
  refreshing?: boolean;
  onRefresh?: () => void;
  contentStyle?: ViewStyle;
  /** Set when a screen renders its own header and should not inset the top. */
  edgeToEdgeTop?: boolean;
  /** Toca fuera de un campo de texto para cerrar el teclado (pantallas de formulario). */
  dismissKeyboardOnTap?: boolean;
}

export function Screen({
  children,
  scroll = true,
  padded = true,
  refreshing,
  onRefresh,
  contentStyle,
  edgeToEdgeTop = false,
  dismissKeyboardOnTap = false,
}: ScreenProps) {
  const insets = useSafeAreaInsets();

  const padding: ViewStyle = {
    paddingTop: edgeToEdgeTop ? 0 : insets.top + spacing.sm,
    paddingBottom: insets.bottom + spacing.xxl,
    paddingHorizontal: padded ? spacing.xl : 0,
  };

  const wrap = (node: ReactNode) =>
    dismissKeyboardOnTap ? (
      <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
        {node}
      </TouchableWithoutFeedback>
    ) : (
      node
    );

  if (!scroll) {
    return wrap(<View style={[styles.root, padding, contentStyle]}>{children}</View>);
  }

  // Cualquier campo dentro de la pantalla sube por encima del teclado.
  return wrap(
    <KeyboardAwareScrollView
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
    </KeyboardAwareScrollView>,
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
});
