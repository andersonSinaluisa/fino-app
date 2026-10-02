import { StyleSheet, View } from 'react-native';
import { colors, radius } from '../../theme';
import { utilizationFill } from '../../utils/creditCards';

interface UtilizationBarProps {
  /** Del backend (CreditCardCalculator). null = tarjeta sin cupo configurado. */
  utilizationPercent: number | null;
  isOverLimit?: boolean;
  height?: number;
}

/**
 * Cupo utilizado. La barra solo refuerza lo que el texto de al lado ya dice
 * ("$750.32 de $2,000 · 37.5%"); el color sube a ámbar pasado el 70 % y al rojo
 * suave de la marca solo si se pasó del cupo.
 */
export function UtilizationBar({ utilizationPercent, isOverLimit = false, height = 8 }: UtilizationBarProps) {
  const fill = utilizationFill(utilizationPercent);
  const color = isOverLimit ? colors.danger : fill >= 70 ? colors.warning : colors.primary;

  return (
    <View
      accessibilityRole="progressbar"
      accessibilityLabel="Cupo utilizado"
      accessibilityValue={{ min: 0, max: 100, now: Math.round(fill) }}
      style={[styles.track, { height, borderRadius: height / 2 }]}
    >
      <View style={[styles.fill, { width: `${fill}%`, backgroundColor: color, borderRadius: height / 2 }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  track: {
    width: '100%',
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
    borderRadius: radius.pill,
  },
  fill: {
    height: '100%',
  },
});
