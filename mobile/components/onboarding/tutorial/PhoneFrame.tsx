import { StyleSheet, View, type ViewStyle } from 'react-native';
import { colors, radius } from '../../../theme';

interface PhoneFrameProps {
  children: React.ReactNode;
  style?: ViewStyle;
}

/**
 * Onboarding funcional: el marco de teléfono genérico donde vive
 * FakeBankScreen. A propósito NO imita un iPhone/Android real -- una barra de
 * estado simplificada y bordes redondeados alcanzan para leerse como "esto es
 * un teléfono" sin competir por atención con el contenido de adentro, que es
 * lo que de verdad hay que mirar.
 */
export function PhoneFrame({ children, style }: PhoneFrameProps) {
  return (
    <View style={[styles.frame, style]}>
      <View style={styles.statusBar}>
        <View style={styles.notch} />
      </View>
      <View style={styles.screen}>{children}</View>
      <View style={styles.homeIndicator} />
    </View>
  );
}

const styles = StyleSheet.create({
  frame: {
    width: 240,
    height: 420,
    borderRadius: radius.xl,
    backgroundColor: colors.primary,
    padding: 6,
    alignSelf: 'center',
  },
  statusBar: {
    height: 22,
    alignItems: 'center',
    justifyContent: 'flex-end',
  },
  notch: {
    width: 60,
    height: 6,
    borderRadius: radius.pill,
    backgroundColor: 'rgba(245, 243, 237, 0.35)',
  },
  screen: {
    flex: 1,
    borderRadius: radius.lg,
    backgroundColor: colors.surface,
    overflow: 'hidden',
  },
  homeIndicator: {
    alignSelf: 'center',
    width: 80,
    height: 4,
    borderRadius: radius.pill,
    backgroundColor: 'rgba(245, 243, 237, 0.35)',
    marginTop: 6,
  },
});
