import { useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, Input, Screen, SectionHeader, Typo } from '../../components/ui';
import { ProviderAvatar } from '../../components/ui/ProviderAvatar';
import { useCreateAccount, useProviders } from '../../hooks/queries';
import type { Provider } from '../../types/api';

const accountTypes = [
  { code: 'Savings', label: 'Ahorros' },
  { code: 'Checking', label: 'Corriente' },
  { code: 'CreditCard', label: 'Tarjeta' },
  { code: 'Wallet', label: 'Billetera' },
] as const;

export default function AddAccountScreen() {
  const router = useRouter();
  const { data: providers, isLoading } = useProviders();
  const createAccount = useCreateAccount();

  const [selected, setSelected] = useState<Provider | null>(null);
  const [alias, setAlias] = useState('');
  const [mask, setMask] = useState('');
  const [accountType, setAccountType] = useState<string>('Savings');
  const [balance, setBalance] = useState('');

  const banks = useMemo(() => (providers ?? []).filter((p) => p.kind === 'Bank'), [providers]);
  const wallets = useMemo(() => (providers ?? []).filter((p) => p.kind === 'Wallet'), [providers]);

  const submit = () => {
    if (!selected) {
      return;
    }

    const parsedBalance = balance.trim().length > 0 ? Number(balance.replace(',', '.')) : null;

    createAccount.mutate(
      {
        providerCode: selected.code,
        alias: alias.trim().length > 0 ? alias.trim() : selected.name,
        accountType,
        connectionMode: selected.defaultMode,
        mask: mask.trim().length > 0 ? mask.trim() : null,
        openingVerifiedBalance: Number.isFinite(parsedBalance as number) ? parsedBalance : null,
        currency: 'USD',
      },
      {
        onSuccess: () => router.back(),
        onError: () =>
          Alert.alert('No pudimos crear la cuenta', 'Revisa los datos e inténtalo de nuevo.'),
      },
    );
  };

  if (selected) {
    return (
      <Screen>
        <Pressable onPress={() => setSelected(null)} hitSlop={12} style={styles.back}>
          <Ionicons name="chevron-back" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Cambiar institución
          </Typo>
        </Pressable>

        <View style={styles.selectedHeader}>
          <ProviderAvatar name={selected.name} color={selected.brandColor} size={52} />
          <View style={styles.selectedText}>
            <Typo variant="heading">{selected.name}</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              {selected.capabilities.map((c) => c.label).join(' · ')}
            </Typo>
          </View>
        </View>

        <View style={styles.form}>
          <Input
            label="Nombre de la cuenta"
            value={alias}
            onChangeText={setAlias}
            placeholder={selected.name}
          />

          <Input
            label="Últimos 4 dígitos"
            value={mask}
            onChangeText={setMask}
            keyboardType="number-pad"
            maxLength={4}
            placeholder="4821"
            hint="Nexo nunca guarda tu número de cuenta completo."
          />

          <View style={styles.typeRow}>
            <Typo variant="overline" color={colors.textSecondary}>
              TIPO DE CUENTA
            </Typo>
            <View style={styles.typeChips}>
              {accountTypes.map((type) => (
                <Pressable
                  key={type.code}
                  onPress={() => setAccountType(type.code)}
                  style={[styles.typeChip, accountType === type.code ? styles.typeChipSelected : null]}
                >
                  <Typo
                    variant="caption"
                    color={accountType === type.code ? colors.onPrimary : colors.text}
                  >
                    {type.label}
                  </Typo>
                </Pressable>
              ))}
            </View>
          </View>

          <Input
            label="Saldo actual (opcional)"
            value={balance}
            onChangeText={setBalance}
            keyboardType="decimal-pad"
            placeholder="1240.50"
            hint="Si lo ingresas, lo tomamos como saldo verificado y estimamos desde ahí."
          />

          <Button
            label="Agregar cuenta"
            onPress={submit}
            loading={createAccount.isPending}
          />
        </View>
      </Screen>
    );
  }

  return (
    <Screen>
      <View style={styles.header}>
        <Typo variant="title">Agregar cuenta</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Elige tu institución. Mostramos solo lo que Nexo puede hacer hoy con cada una.
        </Typo>
      </View>

      {isLoading ? (
        <Typo variant="caption" color={colors.textSecondary}>
          Cargando instituciones…
        </Typo>
      ) : (
        <>
          <SectionHeader title="Bancos" />
          <View style={styles.list}>
            {banks.map((provider) => (
              <ProviderOption key={provider.code} provider={provider} onSelect={setSelected} />
            ))}
          </View>

          <View style={styles.walletSection}>
            <SectionHeader title="Billeteras" />
            <View style={styles.list}>
              {wallets.map((provider) => (
                <ProviderOption key={provider.code} provider={provider} onSelect={setSelected} />
              ))}
            </View>
          </View>
        </>
      )}
    </Screen>
  );
}

function ProviderOption({
  provider,
  onSelect,
}: {
  provider: Provider;
  onSelect: (provider: Provider) => void;
}) {
  const automatic = provider.capabilities.find((capability) => capability.isAutomatic);

  return (
    <Card onPress={() => onSelect(provider)}>
      <View style={styles.option}>
        <ProviderAvatar name={provider.name} color={provider.brandColor} />

        <View style={styles.optionBody}>
          <Typo variant="subheading">{provider.name}</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {provider.capabilities.map((capability) => capability.label).join(' · ')}
          </Typo>
        </View>

        {automatic ? <Badge label="Automática" tone="positive" /> : null}
        <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  header: {
    marginBottom: spacing.xl,
    gap: spacing.xs,
  },
  list: {
    gap: spacing.md,
  },
  walletSection: {
    marginTop: spacing.xxl,
  },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  optionBody: {
    flex: 1,
    gap: 2,
  },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  selectedHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    marginBottom: spacing.xl,
  },
  selectedText: {
    flex: 1,
    gap: 2,
  },
  form: {
    gap: spacing.lg,
  },
  typeRow: {
    gap: spacing.sm,
  },
  typeChips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  typeChip: {
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm + 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  typeChipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
});
