import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Animated, Easing, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { Typo } from './Typo';

/**
 * §6 ("guardado inmediato") y §31 ("notificación después de guardar").
 *
 * Fino no tenía toast ni snackbar: hasta ahora cada acción confirmaba navegando o
 * recargando una pantalla. El registro rápido lo necesita porque la promesa es
 * guardar SIN pedir confirmación, y eso solo es aceptable si deshacerlo está a un
 * toque durante unos segundos. El toast no es decoración: es la mitad del trato.
 *
 * Nada de esto manda una push (§31). Quien acaba de escribir el movimiento ya sabe
 * que lo escribió.
 */

const DEFAULT_DURATION_MS = 5000;

/** §6: "Deshacer debe estar disponible durante unos segundos." */
const UNDO_DURATION_MS = 6000;

export interface ToastAction {
  label: string;
  onPress: () => void;
}

export interface ToastOptions {
  message: string;
  tone?: 'success' | 'error';
  action?: ToastAction;
  durationMs?: number;
}

interface ToastContextValue {
  show: (options: ToastOptions) => void;
  hide: () => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);

  if (!context) {
    throw new Error('useToast necesita estar dentro de <ToastProvider>.');
  }

  return context;
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const insets = useSafeAreaInsets();
  const [toast, setToast] = useState<ToastOptions | null>(null);
  const translateY = useRef(new Animated.Value(120)).current;
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearTimer = useCallback(() => {
    if (timer.current) {
      clearTimeout(timer.current);
      timer.current = null;
    }
  }, []);

  const hide = useCallback(() => {
    clearTimer();
    Animated.timing(translateY, {
      toValue: 120,
      duration: 180,
      easing: Easing.in(Easing.cubic),
      useNativeDriver: true,
    }).start(({ finished }) => {
      if (finished) {
        setToast(null);
      }
    });
  }, [clearTimer, translateY]);

  const show = useCallback(
    (options: ToastOptions) => {
      clearTimer();
      setToast(options);

      // §33: háptica sutil, distinta según el resultado, y respetando siempre la
      // configuración del sistema (por eso el catch: si el usuario la desactivó o
      // el dispositivo no la soporta, no es un error que deba romper nada).
      const feedback =
        options.tone === 'error'
          ? Haptics.NotificationFeedbackType.Warning
          : Haptics.NotificationFeedbackType.Success;
      void Haptics.notificationAsync(feedback).catch(() => undefined);

      Animated.timing(translateY, {
        toValue: 0,
        // §32: 200-300 ms. Rápido, pero no instantáneo: el movimiento es lo que
        // hace que se note que algo pasó.
        duration: 220,
        easing: Easing.out(Easing.cubic),
        useNativeDriver: true,
      }).start();

      const duration =
        options.durationMs ?? (options.action ? UNDO_DURATION_MS : DEFAULT_DURATION_MS);

      timer.current = setTimeout(() => hide(), duration);
    },
    [clearTimer, hide, translateY],
  );

  useEffect(() => clearTimer, [clearTimer]);

  const value = useMemo(() => ({ show, hide }), [show, hide]);

  return (
    <ToastContext.Provider value={value}>
      {children}

      {toast ? (
        <Animated.View
          testID="toast"
          pointerEvents="box-none"
          style={[
            styles.wrapper,
            { paddingBottom: insets.bottom + spacing.xl, transform: [{ translateY }] },
          ]}
        >
          <View
            style={[styles.toast, toast.tone === 'error' ? styles.toastError : null]}
            accessibilityRole="alert"
            accessibilityLiveRegion="polite"
          >
            <Ionicons
              name={toast.tone === 'error' ? 'alert-circle' : 'checkmark-circle'}
              size={20}
              color={toast.tone === 'error' ? colors.danger : colors.accent}
            />

            <Typo variant="body" color={colors.onPrimary} style={styles.message} numberOfLines={2}>
              {toast.message}
            </Typo>

            {toast.action ? (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={toast.action.label}
                // §37: 44px mínimos de área táctil. Deshacer es justamente el
                // control que alguien pulsa con prisa y sin mirar.
                hitSlop={12}
                style={({ pressed }) => [styles.action, pressed ? styles.actionPressed : null]}
                onPress={() => {
                  clearTimer();
                  toast.action?.onPress();
                  hide();
                }}
              >
                <Typo variant="bodyStrong" color={colors.accent}>
                  {toast.action.label}
                </Typo>
              </Pressable>
            ) : null}
          </View>
        </Animated.View>
      ) : null}
    </ToastContext.Provider>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    paddingHorizontal: spacing.lg,
  },
  toast: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: 56,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.lg,
    backgroundColor: colors.primary,
  },
  toastError: {
    backgroundColor: '#3A2320',
  },
  message: {
    flex: 1,
  },
  action: {
    minHeight: 44,
    justifyContent: 'center',
    paddingHorizontal: spacing.sm,
  },
  actionPressed: {
    opacity: 0.6,
  },
});
