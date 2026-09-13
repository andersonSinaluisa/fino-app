import { Modal, Pressable, StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Button, Typo } from '../ui';

interface SkipOnboardingSheetProps {
  visible: boolean;
  loading?: boolean;
  onDismiss: () => void;
  onConfirmSkip: () => void;
}

/**
 * Onboarding funcional: el "Ahora no" nunca sale directo -- primero esto,
 * sin culpa ("Puedes configurar FINO después" en vez de insistir), pero
 * honesto sobre la consecuencia real (sin movimientos no hay análisis
 * todavía). Mismo patrón de bottom sheet que SelectSheet (components/ui),
 * sin traer una librería nueva.
 */
export function SkipOnboardingSheet({ visible, loading, onDismiss, onConfirmSkip }: SkipOnboardingSheetProps) {
  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onDismiss}>
      <Pressable style={styles.backdrop} onPress={onDismiss} accessibilityRole="button" accessibilityLabel="Cerrar" />

      <View style={styles.sheet}>
        <View style={styles.handle} />

        <Typo variant="heading">Puedes configurar Fino después</Typo>
        <Typo variant="body" color={colors.textSecondary} style={styles.body}>
          Sin movimientos todavía no vamos a poder mostrarte tu análisis financiero. Puedes conectar tu banco
          cuando quieras desde Cuentas.
        </Typo>

        <View style={styles.actions}>
          <Button label="Continuar ahora" onPress={onDismiss} fullWidth />
          <Button
            label="Configurar después"
            onPress={onConfirmSkip}
            variant="ghost"
            loading={loading}
            fullWidth
          />
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: colors.overlay,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xxl,
    gap: spacing.sm,
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.sm,
  },
  body: {
    marginBottom: spacing.md,
  },
  actions: {
    gap: spacing.sm,
  },
});
