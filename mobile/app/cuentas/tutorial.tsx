import { useEffect, useMemo, useRef, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { PhoneFrame, FakeBankScreen, TutorialCaption, TutorialProgress } from '../../components/onboarding/tutorial';
import { getBankImportConfig } from '../../lib/bankTutorials';
import { useCompleteOnboardingTutorial, useProviders, useSkipOnboarding } from '../../hooks/queries';
import { useReduceMotion } from '../../hooks/useReduceMotion';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';
import { AnalyticsEvent, track } from '../../services/analytics';

const STEP_DWELL_MS = 3200;

/**
 * Onboarding funcional, Pantalla 4 (el centro del flujo): el tutorial animado
 * armado enteramente desde `lib/bankTutorials` -- este componente no sabe
 * nada de ningún banco en particular, solo sabe leer un `BankTutorialConfig`.
 *
 * Pantalla COMPARTIDA: la abre tanto el onboarding inicial (`onboarding=1`)
 * como "Cuentas → Agregar cuenta" (sin ese parámetro) -- ver `agregar.tsx`.
 * No duplica nada: es la única pantalla de tutorial que existe.
 */
export default function BankTutorialScreen() {
  const router = useRouter();
  const { bankId, accountId, onboarding } = useLocalSearchParams<{
    bankId: string;
    accountId?: string;
    onboarding?: string;
  }>();
  const isOnboarding = onboarding === '1';
  const reduceMotion = useReduceMotion();
  const completeTutorial = useCompleteOnboardingTutorial();
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);
  const { data: providers } = useProviders();

  const config = useMemo(() => (bankId ? getBankImportConfig(bankId) : null), [bankId]);
  const provider = useMemo(() => providers?.find((item) => item.code === bankId) ?? null, [providers, bankId]);
  const bankTitle = config?.tutorial.title ?? provider?.name ?? 'tu banco';
  const brandColor = provider?.brandColor ?? colors.primary;

  const totalSteps = config?.tutorial.steps.length ?? 0;
  const [stepIndex, setStepIndex] = useState(0);

  useEffect(() => {
    if (isOnboarding && config) {
      track(AnalyticsEvent.BankTutorialStarted, { bankCode: bankId });
    }
    // Solo al entrar con un config resuelto -- bankId no cambia dentro de
    // esta pantalla, así que esto corre una vez.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [Boolean(config)]);

  useEffect(() => {
    if (isOnboarding && config) {
      track(AnalyticsEvent.BankTutorialStepViewed, { bankCode: bankId, step: stepIndex + 1 });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stepIndex, Boolean(config)]);
  // Reduce Motion: arranca en modo manual (el usuario avanza a su ritmo) en
  // vez de un demo automático que cambia de pantalla solo cada ~3s.
  const [autoPlay, setAutoPlay] = useState(!reduceMotion);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Construido a mano, no con URLSearchParams -- Hermes/RN no lo trae sin
  // un polyfill que este proyecto no tiene, y ningún otro `router.push` en
  // la app lo usa (todos arman el query string a mano, ver cuentas.tsx).
  const goNext = (params: { accountId?: string; onboarding: boolean }) => {
    const parts = [`bankId=${encodeURIComponent(bankId ?? '')}`];
    if (params.accountId) parts.push(`accountId=${encodeURIComponent(params.accountId)}`);
    if (params.onboarding) parts.push('onboarding=1');
    router.push(`/cuentas/requisitos?${parts.join('&')}`);
  };

  const finish = () => {
    if (timerRef.current) clearTimeout(timerRef.current);
    // Se dispara y se sigue: el spec pide que el tutorial nunca obligue a
    // esperar una llamada de red -- si falla, el usuario ya está en la
    // siguiente pantalla y el estado se reconcilia la próxima vez que algo
    // más toque /api/v1/onboarding/*.
    completeTutorial.mutate();
    if (isOnboarding) {
      track(AnalyticsEvent.BankTutorialCompleted, { bankCode: bankId });
    }
    goNext({ accountId, onboarding: isOnboarding });
  };

  useEffect(() => {
    if (!autoPlay || totalSteps === 0) {
      return;
    }

    timerRef.current = setTimeout(() => {
      if (stepIndex >= totalSteps - 1) {
        finish();
        return;
      }
      setStepIndex((current) => current + 1);
    }, STEP_DWELL_MS);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
    // finish/goNext close over stable values (bankId, accountId, isOnboarding)
    // read fresh via the params above; re-running this effect on every
    // keystroke of those isn't needed since they don't change once the
    // screen is open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoPlay, stepIndex, totalSteps]);

  const goToStep = (index: number) => {
    setAutoPlay(false);
    setStepIndex(Math.max(0, Math.min(totalSteps - 1, index)));
  };

  const replay = () => {
    setStepIndex(0);
    setAutoPlay(true);
  };

  if (!bankId) {
    return (
      <Screen>
        <Typo variant="body" color={colors.textSecondary}>
          Falta elegir un banco antes de ver este tutorial.
        </Typo>
      </Screen>
    );
  }

  const confirmSkip = () => {
    skipOnboarding.mutate(undefined, {
      onSuccess: () => router.replace('/(tabs)'),
    });
  };

  return (
    <Screen scroll={false}>
      {isOnboarding ? (
        <OnboardingTopBar
          stage="conecta"
          onBack={() => router.back()}
          onSkip={() => setSkipSheetVisible(true)}
        />
      ) : (
        <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
          <Ionicons name="chevron-back" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Atrás
          </Typo>
        </Pressable>
      )}

      <View style={styles.header}>
        <Typo variant="title">Así vas a exportar tu archivo</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          {config?.tutorial.verified
            ? `Instrucciones verificadas · ${config.tutorial.lastUpdated}`
            : `Pasos generales para ${bankTitle}. Puede variar un poco según tu versión de la app.`}
        </Typo>
      </View>

      {config ? (
        <View style={styles.demo}>
          <PhoneFrame>
            <FakeBankScreen
              screen={config.tutorial.steps[stepIndex].screen}
              bankTitle={bankTitle}
              brandColor={brandColor}
            />
          </PhoneFrame>

          <TutorialCaption text={config.tutorial.steps[stepIndex].instruction} />

          <TutorialProgress
            step={stepIndex + 1}
            total={totalSteps}
            onPrevious={stepIndex > 0 ? () => goToStep(stepIndex - 1) : undefined}
            onNext={stepIndex < totalSteps - 1 ? () => goToStep(stepIndex + 1) : undefined}
          />

          {!autoPlay ? (
            <Pressable onPress={replay} hitSlop={8} style={styles.replay}>
              <Ionicons name="play-circle-outline" size={16} color={colors.textSecondary} />
              <Typo variant="caption" color={colors.textSecondary}>
                Ver animación completa
              </Typo>
            </Pressable>
          ) : null}
        </View>
      ) : (
        <View style={styles.demo}>
          <Typo variant="body" color={colors.textSecondary}>
            Todavía no armamos un tutorial visual para {bankTitle}, pero puedes seguir: el
            siguiente paso te dice exactamente qué archivo buscar.
          </Typo>
        </View>
      )}

      <Button label="Continuar" onPress={finish} fullWidth />

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
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  demo: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.lg,
    marginBottom: spacing.lg,
  },
  replay: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
});
