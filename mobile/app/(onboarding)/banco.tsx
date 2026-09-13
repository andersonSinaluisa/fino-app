import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Card, Screen, Typo } from '../../components/ui';
import { ProviderAvatar } from '../../components/ui/ProviderAvatar';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';
import { useCreateAccount, useProviders, useSkipOnboarding } from '../../hooks/queries';
import { AnalyticsEvent, track } from '../../services/analytics';
import type { Provider } from '../../types/api';

/**
 * Onboarding funcional, Pantalla 3 ("Elegir Banco"): SOLO bancos reales con
 * import de estado de cuenta -- `kind === 'Bank' && supportsStatementImport`,
 * la misma condición que ya separa bancos de billeteras/wallets-solo-manuales
 * en cuentas/agregar.tsx. Nunca una lista inventada.
 */
export default function ElegirBancoScreen() {
  const router = useRouter();
  const { data: providers, isLoading } = useProviders();
  const createAccount = useCreateAccount();
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);
  const [creatingCode, setCreatingCode] = useState<string | null>(null);

  const importableBanks = useMemo(
    () => (providers ?? []).filter((provider) => provider.kind === 'Bank' && provider.supportsStatementImport),
    [providers],
  );

  const selectBank = async (provider: Provider) => {
    track(AnalyticsEvent.BankSelected, { bankCode: provider.code });
    setCreatingCode(provider.code);
    try {
      const account = await createAccount.mutateAsync({
        providerCode: provider.code,
        alias: provider.name,
        accountType: 'Savings',
        connectionMode: provider.defaultMode,
        mask: null,
        openingVerifiedBalance: null,
        currency: 'USD',
      });
      router.push(`/cuentas/tutorial?bankId=${provider.code}&accountId=${account.id}&onboarding=1`);
    } catch {
      setCreatingCode(null);
    }
  };

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

      <View style={styles.header}>
        <Typo variant="title">¿Cuál es tu banco?</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Te mostramos exactamente cómo exportar tus movimientos desde ahí.
        </Typo>
      </View>

      {isLoading ? (
        <Typo variant="caption" color={colors.textSecondary}>
          Cargando bancos…
        </Typo>
      ) : (
        <View style={styles.list}>
          {importableBanks.map((provider) => (
            <Card key={provider.code} onPress={() => void selectBank(provider)}>
              <View style={styles.row}>
                <ProviderAvatar name={provider.name} color={provider.brandColor} />
                <Typo variant="subheading" style={styles.flex}>
                  {provider.name}
                </Typo>
                {creatingCode === provider.code ? (
                  <Typo variant="caption" color={colors.textSecondary}>
                    Creando…
                  </Typo>
                ) : (
                  <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
                )}
              </View>
            </Card>
          ))}
        </View>
      )}

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
  header: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  list: {
    gap: spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
