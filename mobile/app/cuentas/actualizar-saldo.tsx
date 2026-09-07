import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, Input, Screen, Typo } from '../../components/ui';
import { useAccounts, useSetVerifiedBalance } from '../../hooks/queries';
import { formatCurrency, formatDateInput, parseDateInput } from '../../utils/format';

/**
 * Entregable 11: Cuenta → Actualizar saldo → monto → fecha → confirmar.
 * The account list is already cached by useAccounts() for the accounts tab,
 * so this screen reads from that same cache instead of a second fetch.
 */
export default function UpdateBalanceScreen() {
  const router = useRouter();
  const { accountId } = useLocalSearchParams<{ accountId: string }>();
  const { data: accounts } = useAccounts();
  const account = useMemo(() => accounts?.find((a) => a.id === accountId), [accounts, accountId]);
  const setVerifiedBalance = useSetVerifiedBalance();

  const [balanceText, setBalanceText] = useState(account ? account.balance.toFixed(2) : '');
  const [balanceTouched, setBalanceTouched] = useState(false);
  const [dateText, setDateText] = useState(formatDateInput(new Date()));
  const [submitError, setSubmitError] = useState<string | null>(null);

  // The account list can still be loading (a cold cache, or a deep link
  // straight into this screen) when this component first mounts -- fill the
  // field once real data arrives, but never overwrite what the person typed.
  useEffect(() => {
    if (account && !balanceTouched) {
      setBalanceText(account.balance.toFixed(2));
    }
  }, [account, balanceTouched]);

  const parsedBalance = Number(balanceText.trim().replace(',', '.'));
  const balanceIsValid = balanceText.trim().length > 0 && Number.isFinite(parsedBalance);
  const parsedDate = parseDateInput(dateText);

  if (!account) {
    return (
      <Screen>
        <Typo variant="body" color={colors.textSecondary}>
          No encontramos esa cuenta.
        </Typo>
      </Screen>
    );
  }

  const confirm = () => {
    if (!balanceIsValid || !parsedDate) {
      return;
    }

    setSubmitError(null);
    setVerifiedBalance.mutate(
      { accountId: account.id, balance: parsedBalance, asOf: parsedDate.toISOString() },
      {
        onSuccess: () => router.back(),
        onError: () => setSubmitError('No pudimos actualizar el saldo. Inténtalo de nuevo.'),
      },
    );
  };

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cancelar
        </Typo>
      </Pressable>

      <View style={styles.block}>
        <Typo variant="title">Actualizar saldo</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Ingresa el saldo que viste en {account.alias} y la fecha en la que lo viste. Desde
          ese momento, Fino vuelve a calcular todo con exactitud hasta el próximo movimiento
          que importes.
        </Typo>

        <Card tone="secondary">
          <Typo variant="caption" color={colors.textSecondary}>
            Saldo actual ({account.balanceType === 'Verified' ? 'verificado' : 'estimado'})
          </Typo>
          <Typo variant="heading" tabular>
            {formatCurrency(account.balance)}
          </Typo>
        </Card>

        <Input
          label="Monto"
          value={balanceText}
          onChangeText={(text) => {
            setBalanceTouched(true);
            setBalanceText(text);
          }}
          keyboardType="decimal-pad"
          placeholder="1240.50"
          error={!balanceIsValid && balanceText.trim().length > 0 ? 'Ingresa un monto válido.' : null}
        />

        <View>
          <Input
            label="Fecha del saldo"
            value={dateText}
            onChangeText={setDateText}
            placeholder="DD/MM/AAAA"
            hint="La fecha en la que viste este saldo en tu banco o app."
            error={!parsedDate ? 'Ingresa una fecha real y no futura, formato DD/MM/AAAA.' : null}
          />

          <View style={styles.quickDates}>
            <Pressable style={styles.quickChip} onPress={() => setDateText(formatDateInput(new Date()))}>
              <Typo variant="caption" color={colors.text}>
                Hoy
              </Typo>
            </Pressable>
            <Pressable
              style={styles.quickChip}
              onPress={() => setDateText(formatDateInput(new Date(Date.now() - 86_400_000)))}
            >
              <Typo variant="caption" color={colors.text}>
                Ayer
              </Typo>
            </Pressable>
          </View>
        </View>

        {submitError ? (
          <View style={styles.errorBox}>
            <Ionicons name="alert-circle-outline" size={17} color={colors.danger} />
            <Typo variant="caption" color={colors.danger} style={styles.flex}>
              {submitError}
            </Typo>
          </View>
        ) : null}

        <Button
          label="Confirmar"
          onPress={confirm}
          loading={setVerifiedBalance.isPending}
          disabled={!balanceIsValid || !parsedDate}
        />
      </View>
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
  block: {
    gap: spacing.lg,
  },
  quickDates: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
  quickChip: {
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
  },
  errorBox: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: 'rgba(216, 102, 91, 0.12)',
    borderRadius: radius.md,
    padding: spacing.md,
  },
});
