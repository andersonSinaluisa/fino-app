import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useState } from 'react';
import { colors, radius, spacing } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';
import { AnimatedArrow, AnimatedFile } from '../../components/onboarding/tutorial';
import { useSkipOnboarding } from '../../hooks/queries';

/**
 * Onboarding funcional, Pantalla 2: el archivo viajando de tu banco a Fino --
 * la explicación completa de "cómo funciona" en una imagen, no un párrafo de
 * jerga ("sincronización", "ingesta"). Reutiliza AnimatedFile/AnimatedArrow
 * (Fase 2), los mismos primitivos que arma BankTutorial más adelante.
 */
export default function ComoFuncionaScreen() {
  const router = useRouter();
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);

  const confirmSkip = () => {
    skipOnboarding.mutate(undefined, {
      onSuccess: () => router.replace('/(tabs)'),
    });
  };

  return (
    <Screen>
      <OnboardingTopBar
        stage="conecta"
        onBack={() => router.back()}
        onSkip={() => setSkipSheetVisible(true)}
      />

      <View style={styles.body}>
        <View style={styles.diagram}>
          <View style={styles.node}>
            <Ionicons name="business-outline" size={26} color={colors.text} />
            <Typo variant="caption" color={colors.textSecondary}>
              Tu banco
            </Typo>
          </View>

          <AnimatedArrow direction="right" />
          <AnimatedFile />
          <AnimatedArrow direction="right" />

          <View style={styles.node}>
            <Ionicons name="analytics-outline" size={26} color={colors.text} />
            <Typo variant="caption" color={colors.textSecondary}>
              Fino
            </Typo>
          </View>
        </View>

        <View style={styles.copy}>
          <Typo variant="title" align="center">
            Descargas un archivo, Fino lo lee
          </Typo>
          <Typo variant="body" color={colors.textSecondary} align="center">
            Tú decides cuándo. Descargas el estado de cuenta desde la app de tu banco y lo subes aquí. Fino
            organiza cada movimiento por ti -- nunca se conecta directo a tu banco.
          </Typo>
        </View>
      </View>

      <Button label="Elegir mi banco" onPress={() => router.push('/(onboarding)/banco')} fullWidth />

      <SkipOnboardingSheet
        visible={skipSheetVisible}
        loading={skipOnboarding.isPending}
        onDismiss={() => setSkipSheetVisible(false)}
        onConfirmSkip={confirmSkip}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  body: {
    flex: 1,
    justifyContent: 'center',
    gap: spacing.xxl,
  },
  diagram: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
  },
  node: {
    alignItems: 'center',
    gap: spacing.xs,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.md,
    width: 76,
  },
  copy: {
    gap: spacing.sm,
    paddingHorizontal: spacing.md,
  },
});
