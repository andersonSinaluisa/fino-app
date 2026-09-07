import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../../theme';
import { Typo } from '../../../components/ui';
import { OnboardingHeader } from '../../../components/onboarding/OnboardingHeader';
import { ConnectionOption } from '../../../components/onboarding/ConnectionOption';

type ConnectionType = 'banks' | 'wallets' | 'file';

function Chip({ label, highlighted = false }: { label: string; highlighted?: boolean }) {
  return (
    <View style={[chipStyles.chip, highlighted ? chipStyles.chipHighlighted : null]}>
      <Typo style={[chipStyles.chipText, highlighted ? chipStyles.chipTextHighlighted : null]}>{label}</Typo>
    </View>
  );
}

const chipStyles = StyleSheet.create({
  chip: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: 5,
    backgroundColor: colors.background,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
  },
  chipHighlighted: {
    backgroundColor: colors.text,
    borderColor: colors.text,
  },
  chipText: {
    fontSize: 11,
    fontWeight: '600',
    color: colors.textSecondary,
  },
  chipTextHighlighted: {
    fontSize: 11,
    fontWeight: '600',
    color: colors.onPrimary,
  },
});

/**
 * Onboarding Step 2 -- `design/paso_2_onboarding_nexo/code.html`. Lets a
 * first-time visitor pick what they mean to connect before creating their
 * account. The choice itself only decides where registration sends them
 * next (see register.tsx): every option converges on the same real "add
 * account" screen (app/cuentas/agregar.tsx), which already lists the real
 * banks and wallets and is where a CSV/Excel import starts from too.
 */
export default function OnboardingStepTwo() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const [selected, setSelected] = useState<ConnectionType>('banks');

  const goToRegister = () => {
    router.push({ pathname: '/(auth)/register', params: { intent: 'connect' } });
  };

  return (
    <View style={styles.root}>
      <View
        style={[styles.header, { paddingTop: insets.top + spacing.md }]}
      >
        <OnboardingHeader step={2} onBack={() => router.back()} />
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={[styles.scrollContent, { paddingBottom: insets.bottom + 160 }]}
      >
        <View style={styles.hero}>
          <Typo style={styles.title}>¿Qué quieres conectar?</Typo>
          <Typo style={styles.subtitle}>
            Elige tu banco, billetera o sube un estado de cuenta.{' '}
            <Typo style={styles.subtitleStrong}>No pediremos contraseñas bancarias.</Typo>
          </Typo>
        </View>

        <View style={styles.options}>
          <ConnectionOption
            icon="business-outline"
            iconBackground="#F6F4EB"
            iconColor="#8A5A00"
            title="Bancos locales"
            tagLabel="Lectura protegida"
            tagTone="positive"
            description="Pichincha, Guayaquil, Produbanco, Pacífico"
            selected={selected === 'banks'}
            onSelect={() => setSelected('banks')}
            footer={
              <View style={styles.chipsRow}>
                <Chip label="Pichincha" highlighted />
                <Chip label="Guayaquil" />
                <Chip label="Produbanco" />
                <Chip label="Pacífico" />
              </View>
            }
          />

          <ConnectionOption
            icon="wallet-outline"
            iconBackground="#E8F8F0"
            iconColor="#1E7A63"
            title="Billeteras digitales"
            tagLabel="⚡ Instantáneo"
            tagTone="accent"
            description="Sincroniza tus transacciones del día a día"
            selected={selected === 'wallets'}
            onSelect={() => setSelected('wallets')}
            footer={
              <View style={styles.chipsRow}>
                <Chip label="DEUNA" />
                <Chip label="PayPhone" />
                <Chip label="PeiGo" />
              </View>
            }
          />

          <ConnectionOption
            icon="document-text-outline"
            iconBackground="#EFF6FF"
            iconColor="#1D5FC7"
            title="Importar archivo"
            tagLabel="CSV o Excel"
            tagTone="neutral"
            description="Sube el extracto descargado desde tu banco sin conectar credenciales directas."
            selected={selected === 'file'}
            onSelect={() => setSelected('file')}
            footer={
              <View style={styles.privateRow}>
                <Ionicons name="checkmark-circle" size={14} color={colors.success} />
                <Typo style={styles.privateRowText}>100% privado · Sin vincular accesos</Typo>
              </View>
            }
          />
        </View>

        <View style={styles.securityBanner}>
          <View style={styles.securityIcon}>
            <Ionicons name="shield-checkmark-outline" size={17} color={colors.text} />
          </View>
          <View style={styles.securityText}>
            <Typo style={styles.securityTitle}>Tranquilidad &amp; Privacidad</Typo>
            <Typo style={styles.securityBody}>
              Fino utiliza cifrado punto a punto AES-256 de nivel bancario. Solo tú tienes control y visibilidad
              sobre tus datos. Nunca realizaremos cargos ni transferencias.
            </Typo>
          </View>
        </View>
      </ScrollView>

      <View style={[styles.footer, { paddingBottom: insets.bottom + spacing.lg }]}>
        <Pressable
          style={({ pressed }) => [styles.primaryCta, pressed ? styles.pressed : null]}
          onPress={goToRegister}
          accessibilityRole="button"
        >
          <Typo style={styles.primaryCtaLabel}>Continuar</Typo>
          <View style={styles.primaryCtaIcon}>
            <Ionicons name="arrow-forward" size={16} color={colors.text} />
          </View>
        </Pressable>

        <Pressable
          style={({ pressed }) => [styles.laterButton, pressed ? styles.pressed : null]}
          onPress={() => router.push('/(auth)/register')}
          accessibilityRole="button"
        >
          <Typo style={styles.laterLabel}>Lo haré más tarde o explorar con datos demo</Typo>
        </Pressable>

        <Typo style={styles.disclaimer}>Protegido con cifrado punto a punto AES-256</Typo>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  header: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.sm,
  },
  scrollContent: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.sm,
    gap: spacing.xl,
  },
  hero: {
    gap: 6,
  },
  title: {
    fontSize: 26,
    lineHeight: 31,
    fontWeight: '800',
    letterSpacing: -0.6,
    color: colors.text,
  },
  subtitle: {
    fontSize: 14,
    lineHeight: 19,
    color: colors.textSecondary,
  },
  subtitleStrong: {
    fontSize: 14,
    lineHeight: 19,
    fontWeight: '600',
    color: colors.text,
  },
  options: {
    gap: spacing.md,
  },
  chipsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs,
  },
  privateRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  privateRowText: {
    fontSize: 11,
    fontWeight: '600',
    color: colors.success,
  },
  securityBanner: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.xl,
    padding: spacing.lg,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
  },
  securityIcon: {
    width: 36,
    height: 36,
    borderRadius: 14,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  securityText: {
    flex: 1,
    gap: 3,
  },
  securityTitle: {
    fontSize: 12,
    fontWeight: '700',
    color: colors.text,
  },
  securityBody: {
    fontSize: 11,
    lineHeight: 15,
    color: colors.textSecondary,
  },
  footer: {
    position: 'absolute',
    left: 0,
    right: 0,
    bottom: 0,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    backgroundColor: colors.background,
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
    fontSize: 15,
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
  laterButton: {
    paddingVertical: spacing.sm,
    alignItems: 'center',
  },
  laterLabel: {
    fontSize: 12,
    fontWeight: '600',
    color: colors.textSecondary,
  },
  disclaimer: {
    fontSize: 10,
    color: colors.textSecondary,
    textAlign: 'center',
  },
  pressed: {
    opacity: 0.9,
    transform: [{ scale: 0.99 }],
  },
});
