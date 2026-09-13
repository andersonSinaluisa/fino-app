import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { AnimatedFile } from '../../components/onboarding/tutorial';
import { getBankImportConfig } from '../../lib/bankTutorials';
import { useSkipOnboarding } from '../../hooks/queries';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';

const FORMAT_LABEL: Record<string, string> = {
  csv: 'CSV',
  xls: 'XLS',
  xlsx: 'XLSX',
};

/**
 * Onboarding funcional, Pantalla 5 ("Antes de continuar"): qué archivo buscar,
 * en el lenguaje del banco elegido -- nunca "sube tu dataset". Los formatos
 * que se muestran salen SIEMPRE de `BankImportConfig.supportedFormats`
 * (validado en dev contra `ACCEPTED_EXTENSIONS`, ver lib/bankTutorials/index.ts),
 * así que esta pantalla nunca puede prometer un formato que el importador
 * real rechace.
 *
 * Pantalla COMPARTIDA con "Cuentas → Agregar cuenta" -- ver tutorial.tsx.
 */
export default function BankFileRequirementsScreen() {
  const router = useRouter();
  const { bankId, accountId, onboarding } = useLocalSearchParams<{
    bankId: string;
    accountId?: string;
    onboarding?: string;
  }>();
  const isOnboarding = onboarding === '1';
  const config = bankId ? getBankImportConfig(bankId) : null;
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);

  const confirmSkip = () => {
    skipOnboarding.mutate(undefined, {
      onSuccess: () => router.replace('/(tabs)'),
    });
  };

  // Construido a mano, no con URLSearchParams -- ver la misma nota en
  // tutorial.tsx.
  const goToImport = () => {
    const parts: string[] = [];
    if (accountId) parts.push(`accountId=${encodeURIComponent(accountId)}`);
    if (isOnboarding) parts.push('onboarding=1');
    if (bankId) parts.push(`bankId=${encodeURIComponent(bankId)}`);
    router.push(`/cuentas/importar?${parts.join('&')}`);
  };

  return (
    <Screen>
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
        <Typo variant="title">Antes de continuar</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          {config?.fileHint ?? 'Descarga el archivo de movimientos desde tu banco, tal como te lo entrega.'}
        </Typo>
      </View>

      <View style={styles.illustration}>
        <AnimatedFile label={config?.displayName ?? 'tu banco'} size={56} />
      </View>

      <View style={styles.formats}>
        <Typo variant="overline" color={colors.textSecondary}>
          FORMATOS QUE ACEPTAMOS
        </Typo>
        <View style={styles.chips}>
          {(config?.supportedFormats ?? ['csv', 'xlsx']).map((format) => (
            <View key={format} style={styles.chip}>
              <Typo variant="caption" color={colors.text}>
                {FORMAT_LABEL[format] ?? format.toUpperCase()}
              </Typo>
            </View>
          ))}
        </View>
      </View>

      <View style={styles.privacy}>
        <Ionicons name="lock-closed-outline" size={15} color={colors.textSecondary} />
        <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
          Tú decides qué compartes. Cómo usamos tus datos.
        </Typo>
      </View>

      <Button label="Elegir archivo" onPress={goToImport} fullWidth />

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
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  header: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  illustration: {
    alignItems: 'center',
    marginBottom: spacing.xl,
  },
  formats: {
    gap: spacing.sm,
    marginBottom: spacing.xxl,
  },
  chips: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  chip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
  },
  privacy: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
});
