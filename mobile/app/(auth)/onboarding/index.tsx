import { useEffect } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../../theme';
import { Typo } from '../../../components/ui';
import { OnboardingHeader } from '../../../components/onboarding/OnboardingHeader';
import { HeroIllustration } from '../../../components/onboarding/HeroIllustration';
import { useOnboardingStore } from '../../../store/onboardingStore';

const pillars: Array<{ icon: keyof typeof Ionicons.glyphMap; title: string; subtitle: string }> = [
  { icon: 'lock-closed-outline', title: '100% Privado', subtitle: 'Tus datos son tuyos' },
  { icon: 'infinite-outline', title: 'Cero Comisiones', subtitle: 'Uso personal libre' },
  { icon: 'shield-checkmark-outline', title: 'Sin Contraseñas', subtitle: 'Solo lectura segura' },
];

/**
 * Onboarding Step 1 -- `design/onboarding_nexo/code.html`. The pitch screen a
 * first-time, anonymous visitor lands on (see the redirect logic in
 * app/index.tsx). Purely presentational: no data fetching, nothing that
 * requires a session.
 */
export default function OnboardingStepOne() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const markSeen = useOnboardingStore((state) => state.markSeen);

  useEffect(() => {
    // Reaching onboarding once is enough for it to never show again on this
    // device -- not tied to whether the person finishes it.
    markSeen();
  }, [markSeen]);

  return (
    <View style={styles.root}>
      <View style={[styles.glow, styles.glowTopLeft]} />
      <View style={[styles.glow, styles.glowBottomRight]} />

      <View style={[styles.content, { paddingTop: insets.top + spacing.md, paddingBottom: insets.bottom + spacing.lg }]}>
        <OnboardingHeader step={1} />

        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.scrollContent}>
          <HeroIllustration />

          <View style={styles.narrative}>
            <View style={styles.headlineWrap}>
              <Typo style={styles.headline}>Todo tu dinero.</Typo>
              <View style={styles.headlineHighlightRow}>
                <View style={styles.headlineHighlightBar} pointerEvents="none" />
                <Typo style={styles.headline}>En un solo lugar.</Typo>
              </View>
            </View>
            <Typo style={styles.body}>
              Mira tus bancos y billeteras sin tener que saltar entre aplicaciones. Entiende tus gastos en segundos,
              sin complicaciones.
            </Typo>

            <View style={styles.pillarsRow}>
              {pillars.map((pillar) => (
                <View key={pillar.title} style={styles.pillar}>
                  <View style={styles.pillarIcon}>
                    <Ionicons name={pillar.icon} size={15} color={colors.text} />
                  </View>
                  <Typo style={styles.pillarTitle}>{pillar.title}</Typo>
                  <Typo style={styles.pillarSubtitle}>{pillar.subtitle}</Typo>
                </View>
              ))}
            </View>
          </View>
        </ScrollView>

        <View style={styles.footer}>
          <Pressable
            style={({ pressed }) => [styles.primaryCta, pressed ? styles.pressed : null]}
            onPress={() => router.push('/(auth)/onboarding/conectar')}
            accessibilityRole="button"
          >
            <Typo style={styles.primaryCtaLabel}>Empezar ahora</Typo>
            <View style={styles.primaryCtaIcon}>
              <Ionicons name="arrow-forward" size={16} color={colors.text} />
            </View>
          </Pressable>

          <Pressable
            style={({ pressed }) => [styles.secondaryCta, pressed ? styles.pressed : null]}
            onPress={() => router.push('/(auth)/login')}
            accessibilityRole="button"
          >
            <Typo style={styles.secondaryCtaLabel}>Ya tengo una cuenta</Typo>
          </Pressable>

          <Typo style={styles.disclaimer}>Protegido con cifrado punto a punto AES-256</Typo>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
    overflow: 'hidden',
  },
  glow: {
    position: 'absolute',
    borderRadius: 999,
  },
  glowTopLeft: {
    width: 260,
    height: 260,
    top: -90,
    left: -90,
    backgroundColor: 'rgba(199, 243, 107, 0.28)',
  },
  glowBottomRight: {
    width: 280,
    height: 280,
    top: 320,
    right: -110,
    backgroundColor: 'rgba(141, 217, 182, 0.22)',
  },
  content: {
    flex: 1,
    paddingHorizontal: spacing.xl,
  },
  scrollContent: {
    flexGrow: 1,
    justifyContent: 'center',
    paddingVertical: spacing.lg,
  },
  narrative: {
    alignItems: 'center',
    marginTop: spacing.md,
  },
  headlineWrap: {
    alignItems: 'center',
  },
  headlineHighlightRow: {
    position: 'relative',
  },
  headlineHighlightBar: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 4,
    height: 10,
    borderRadius: 6,
    backgroundColor: colors.accent,
    opacity: 0.6,
  },
  headline: {
    fontSize: 32,
    lineHeight: 37,
    fontWeight: '800',
    letterSpacing: -0.8,
    color: colors.text,
    textAlign: 'center',
  },
  body: {
    fontSize: 14,
    lineHeight: 20,
    color: colors.textSecondary,
    textAlign: 'center',
    marginTop: spacing.sm,
    paddingHorizontal: spacing.sm,
  },
  pillarsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    width: '100%',
    marginTop: spacing.lg,
    paddingTop: spacing.lg,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.surfaceSecondary,
  },
  pillar: {
    flex: 1,
    alignItems: 'center',
    gap: 3,
  },
  pillarIcon: {
    width: 26,
    height: 26,
    borderRadius: 13,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: 2,
  },
  pillarTitle: {
    fontSize: 10,
    fontWeight: '700',
    color: colors.text,
  },
  pillarSubtitle: {
    fontSize: 9,
    color: colors.textSecondary,
    textAlign: 'center',
  },
  footer: {
    gap: spacing.sm,
  },
  primaryCta: {
    height: 56,
    borderRadius: radius.pill,
    backgroundColor: colors.primary,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
  },
  primaryCtaLabel: {
    fontSize: 16,
    fontWeight: '700',
    color: colors.onPrimary,
  },
  primaryCtaIcon: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  secondaryCta: {
    height: 44,
    borderRadius: radius.pill,
    backgroundColor: 'rgba(255,255,255,0.7)',
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  secondaryCtaLabel: {
    fontSize: 14,
    fontWeight: '600',
    color: colors.text,
  },
  disclaimer: {
    fontSize: 11,
    color: colors.textSecondary,
    textAlign: 'center',
    marginTop: 2,
  },
  pressed: {
    opacity: 0.9,
    transform: [{ scale: 0.99 }],
  },
});
