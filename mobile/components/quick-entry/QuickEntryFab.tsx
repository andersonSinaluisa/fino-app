import { Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { useQuickEntryStore, type QuickEntrySource } from '../../store/quickEntryStore';

/**
 * §1 ("botón global de registro") y §26 ("no duplicar FABs").
 *
 * Hay UN solo botón "+", montado una vez en el layout de las pestañas y no en cada
 * pantalla. Eso no es solo higiene de código: si Home y Movimientos tuvieran cada
 * uno el suyo, acabarían divergiendo en posición, en tamaño y --lo peor-- en lo que
 * hacen al pulsarlos.
 *
 * §13: mantener pulsado abre el sheet ya escuchando. Tocar y mantener son dos
 * gestos sobre el MISMO botón porque el §13 los define así ("mantener presionado el
 * botón +"), y porque un segundo botón de micrófono en la pantalla principal sería
 * ruido para quien nunca usa la voz.
 */

interface QuickEntryFabProps {
  source: QuickEntrySource;
  /** Alto de la barra de pestañas, para que el botón se apoye justo encima. */
  bottomOffset?: number;
}

export function QuickEntryFab({ source, bottomOffset = 0 }: QuickEntryFabProps) {
  const insets = useSafeAreaInsets();
  const open = useQuickEntryStore((state) => state.open);

  return (
    <View
      pointerEvents="box-none"
      style={[styles.wrapper, { bottom: bottomOffset + insets.bottom + spacing.lg }]}
    >
      <Pressable
        testID="quick-entry-fab"
        accessibilityRole="button"
        accessibilityLabel="Registrar efectivo"
        accessibilityHint="Mantén pulsado para dictarlo por voz"
        onPress={() => {
          void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => undefined);
          open(source);
        }}
        onLongPress={() => {
          void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium).catch(() => undefined);
          open(source, { withVoice: true });
        }}
        delayLongPress={350}
        style={({ pressed }) => [styles.fab, pressed ? styles.pressed : null]}
      >
        <Ionicons name="add" size={30} color={colors.onAccent} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    position: 'absolute',
    right: spacing.lg,
    alignItems: 'flex-end',
  },
  fab: {
    width: 60,
    height: 60,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    // El lima de Fino, el único acento de alta energía de la paleta. Se usa poco a
    // propósito, y este es exactamente el sitio donde se justifica: es la acción
    // que más veces al día se toca.
    backgroundColor: colors.accent,
    // §32: "sin sombras fuertes." Lo justo para separarlo del contenido.
    shadowColor: colors.primary,
    shadowOpacity: 0.16,
    shadowRadius: 16,
    shadowOffset: { width: 0, height: 6 },
    elevation: 4,
  },
  pressed: {
    opacity: 0.9,
    transform: [{ scale: 0.96 }],
  },
});
