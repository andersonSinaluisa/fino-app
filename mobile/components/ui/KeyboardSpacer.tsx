import { View } from 'react-native';
import { useKeyboardInset } from '../../hooks/useKeyboardInset';
import { colors } from '../../theme';

/**
 * Para hojas inferiores dentro de un Modal: va justo después de la hoja y
 * ocupa el alto del teclado, así la hoja (y su campo) suben por encima de él.
 * Del color de la hoja para que se vea como una sola pieza.
 */
export function KeyboardSpacer({ color = colors.background }: { color?: string }) {
  const inset = useKeyboardInset();
  if (inset === 0) {
    return null;
  }

  return <View style={{ height: inset, backgroundColor: color }} />;
}
