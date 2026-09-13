import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SourcesToFinoAnimation } from '../../components/onboarding/SourcesToFinoAnimation';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';
import { useSkipOnboarding, useStartOnboarding } from '../../hooks/queries';
import { AnalyticsEvent, track } from '../../services/analytics';
import { useEffect, useState } from 'react';

/**
 * Onboarding funcional, Pantalla 1: "vamos a poner Fino a funcionar", no una
 * lista de funcionalidades. Marca `onboarding_started` en el backend al
 * entrar (Fase 1) -- no en app/index.tsx, porque llegar aquí es lo que de
 * verdad significa "empezó", a diferencia de solo haber iniciado sesión.
 */
export default function BienvenidaScreen() {
  const router = useRouter();
  const startOnboarding = useStartOnboarding();
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);

  useEffect(() => {
    startOnboarding.mutate();
    track(AnalyticsEvent.OnboardingStarted);
    // Se dispara una sola vez al entrar; mutate() es idempotente del lado
    // del backend (User.StartOnboarding no pisa un timestamp ya puesto), así
    // que no hace falta un ref-guard aquí.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const confirmSkip = () => {
    skipOnboarding.mutate(undefined, {
      onSuccess: () => router.replace('/(tabs)'),
    });
  };

  return (
    <Screen>
      <OnboardingTopBar stage="conecta" onSkip={() => setSkipSheetVisible(true)} />

      <View style={styles.body}>
        <SourcesToFinoAnimation />

        <View style={styles.copy}>
          <Typo variant="title" align="center">
            Todo tu dinero, en un solo lugar
          </Typo>
          <Typo variant="body" color={colors.textSecondary} align="center">
            Conecta tus bancos, billeteras y correo. Fino junta todo y te muestra qué está pasando de verdad
            con tu plata.
          </Typo>
        </View>
      </View>

      <Button label="Empezar" onPress={() => router.push('/(onboarding)/como-funciona')} fullWidth />

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
  copy: {
    gap: spacing.sm,
    paddingHorizontal: spacing.md,
  },
});
