import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { useToast } from '../../components/ui/Toast';
import { useQuickEntryBootstrap, useSetCashBalance } from '../../hooks/queries';
import { formatCurrency } from '../../utils/format';

/**
 * §24 ("saldo de efectivo") y §25 ("corrección de saldo").
 *
 * Los dos casos comparten pantalla porque la pregunta que le hacen a la persona es
 * la misma -- "¿cuánto efectivo tienes?" -- y lo único que cambia es qué hace Fino
 * con la respuesta:
 *
 *  - La primera vez (`?modo=inicial`) no hay historial que explicar, así que el
 *    saldo simplemente se ancla. §24: y si no lo declara, no pasa nada, se puede
 *    registrar movimientos igual. Por eso "Ahora no" es un botón de verdad y no
 *    letra pequeña.
 *  - Después, la diferencia entre lo que Fino estimaba y lo que la persona tiene se
 *    registra como un movimiento de AJUSTE visible. §25 es tajante: "no modificar
 *    silenciosamente movimientos históricos. Mantener trazabilidad." Nadie debería
 *    descubrir seis meses después que su saldo cambió y no hay forma de saber por
 *    qué.
 */
export default function AdjustCashScreen() {
  const router = useRouter();
  const toast = useToast();
  const params = useLocalSearchParams<{ modo?: string }>();

  const initialSetup = params.modo === 'inicial';

  const { data: bootstrap } = useQuickEntryBootstrap();
  const setCashBalance = useSetCashBalance();

  const [text, setText] = useState('');

  const estimated = bootstrap?.cashBalance ?? 0;
  const balance = useMemo(() => {
    const value = Number(text.replace(',', '.'));
    return Number.isFinite(value) && text.trim().length > 0 ? value : null;
  }, [text]);

  const difference = balance !== null ? Math.round((balance - estimated) * 100) / 100 : null;
  const canSave = balance !== null && balance >= 0 && !setCashBalance.isPending;

  const submit = () => {
    if (balance === null) {
      return;
    }

    setCashBalance.mutate(
      { balance, mode: initialSetup ? 'Anchor' : 'Adjustment' },
      {
        onSuccess: () => {
          router.back();
          toast.show({
            message: initialSetup
              ? `Tu efectivo quedó en ${formatCurrency(balance)}`
              : `Efectivo ajustado a ${formatCurrency(balance)}`,
          });
        },
        onError: () =>
          toast.show({ message: 'No pudimos guardar el saldo. Inténtalo otra vez.', tone: 'error' }),
      },
    );
  };

  return (
    <Screen>
      <View style={styles.header}>
        <Typo variant="title">
          {initialSetup ? '¿Cuánto efectivo tienes?' : 'Ajustar efectivo'}
        </Typo>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Cerrar"
          onPress={() => router.back()}
          hitSlop={12}
        >
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <Typo variant="body" color={colors.textSecondary} style={styles.intro}>
        {initialSetup
          ? 'Un número aproximado basta. Sirve para que Fino sepa de dónde parte tu efectivo; puedes cambiarlo cuando quieras.'
          : `Fino estima que tienes ${formatCurrency(estimated)}. Cuenta lo que llevas encima y escríbelo aquí.`}
      </Typo>

      <View style={styles.amountRow}>
        <Typo style={styles.currency}>$</Typo>
        <TextInput
          value={text}
          onChangeText={setText}
          keyboardType="decimal-pad"
          placeholder="0.00"
          placeholderTextColor={colors.textSecondary}
          style={styles.amountInput}
          accessibilityLabel="Efectivo que tienes"
          autoFocus
        />
      </View>

      {/* Se dice EXACTAMENTE qué va a pasar antes de que pase. Un ajuste crea un
          movimiento, y eso la persona tiene que saberlo antes de tocar Guardar, no
          descubrirlo luego en su lista. */}
      {!initialSetup && difference !== null && difference !== 0 ? (
        <View style={styles.explain}>
          <Ionicons name="information-circle-outline" size={18} color={colors.textSecondary} />
          <Typo variant="caption" color={colors.textSecondary} style={styles.explainText}>
            Se registrará un ajuste de {difference > 0 ? '+' : '−'}
            {formatCurrency(Math.abs(difference))}. Tus movimientos anteriores no se tocan.
          </Typo>
        </View>
      ) : null}

      <View style={styles.actions}>
        <Button
          label={initialSetup ? 'Guardar' : 'Ajustar efectivo'}
          onPress={submit}
          disabled={!canSave}
          loading={setCashBalance.isPending}
          loadingLabel="Guardando..."
          softDisabled
        />

        {/* §24: "Ahora no" es una salida legítima, no una trampa. No declarar el
            efectivo nunca bloquea el registro de movimientos. */}
        {initialSetup ? (
          <Button label="Ahora no" variant="ghost" onPress={() => router.back()} />
        ) : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  intro: {
    marginBottom: spacing.xl,
  },
  amountRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
  },
  currency: {
    fontSize: typography.title.fontSize,
    color: colors.textSecondary,
  },
  amountInput: {
    flex: 1,
    minHeight: 64,
    color: colors.text,
    fontSize: typography.title.fontSize,
    fontWeight: '700',
  },
  explain: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.lg,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.surfaceSecondary,
  },
  explainText: {
    flex: 1,
  },
  actions: {
    marginTop: spacing.xl,
    gap: spacing.sm,
  },
});
