import { View, StyleSheet } from 'react-native';
import { colors } from '../../theme';

type Side = 'left' | 'right';

const SLICE_WIDTH = 6;
// De cerca del contenido (casi transparente) a pegado al borde (casi opaco).
const OPACITY_STEPS = [0.06, 0.16, 0.32, 0.52, 0.74, 0.92];

/**
 * Cuando una fila de chips horizontal (filtros de Movimientos, por ejemplo)
 * tiene más contenido del que entra en pantalla, el último chip visible
 * queda cortado justo por el borde de la pantalla -- se ve roto en vez de
 * "deslizá para ver más". No hay ninguna librería de gradientes instalada
 * en el proyecto (ni expo-linear-gradient ni react-native-svg), así que
 * esto simula un degradado apilando unas tiras finas semitransparentes del
 * mismo color de fondo -- sin ninguna dependencia nativa nueva, funciona
 * en la build actual sin recompilar.
 *
 * Usar dentro de un contenedor con `position: 'relative'` que envuelva el
 * ScrollView horizontal, con `pointerEvents="none"` implícito para nunca
 * bloquear los toques sobre los chips.
 */
export function EdgeFade({ side }: { side: Side }) {
  const steps = side === 'left' ? [...OPACITY_STEPS].reverse() : OPACITY_STEPS;

  return (
    <View style={[styles.container, side === 'left' ? styles.left : styles.right]} pointerEvents="none">
      {steps.map((opacity, index) => (
        <View
          key={index}
          style={[styles.slice, { left: index * SLICE_WIDTH, opacity }]}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    width: SLICE_WIDTH * OPACITY_STEPS.length,
  },
  left: {
    left: 0,
  },
  right: {
    right: 0,
  },
  slice: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    width: SLICE_WIDTH,
    backgroundColor: colors.background,
  },
});
