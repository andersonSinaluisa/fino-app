import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Card, Chip, Input, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useCreateInstallmentPlan, useTransaction } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { ApiError } from '../../services/apiClient';
import { AnalyticsEvent, installmentsBucket, track } from '../../services/analytics';
import { formatCurrency } from '../../utils/format';

const COMMON_COUNTS = [3, 6, 9, 12, 18, 24, 36];
const MIN_COUNT = 2;
const MAX_COUNT = 72;

/**
 * "Diferir en cuotas" una compra de la tarjeta. La compra NO cambia: sigue
 * siendo un solo gasto del día en que se hizo. El plan solo dice cómo esa
 * deuda se vuelve exigible estado por estado -- y por eso Comprometido reserva
 * solo la cuota del próximo estado. Las cuotas exactas (centavos incluidos) las
 * calcula el backend al guardar.
 */
export default function DeferPurchaseScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string; transactionId: string }>();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data: purchase } = useTransaction(params.transactionId);
  const create = useCreateInstallmentPlan();

  const [count, setCount] = useState<number | null>(12);
  const [customText, setCustomText] = useState('');
  const [rateText, setRateText] = useState('');
  const [error, setError] = useState<string | null>(null);

  if (!purchase) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const custom = customText.trim().length > 0 ? Number(customText) : null;
  const installments = custom ?? count;
  const countValid = installments !== null && Number.isInteger(installments) && installments >= MIN_COUNT && installments <= MAX_COUNT;
  const rate = rateText.trim().length > 0 ? Number(rateText.replace(',', '.')) : null;
  const rateValid = rate === null || (Number.isFinite(rate) && rate >= 0 && rate <= 100);

  const save = () => {
    if (!countValid || !rateValid || installments === null) {
      return;
    }

    setError(null);
    create.mutate(
      {
        id: params.id,
        transactionId: purchase.id,
        numberOfInstallments: installments,
        interestRate: rate,
      },
      {
        onSuccess: () => {
          track(AnalyticsEvent.CreditCardInstallmentCreated, { installmentsBucket: installmentsBucket(installments) });
          router.replace({ pathname: '/tarjetas/[id]', params: { id: params.id, tab: 'installments' } });
        },
        onError: (caught) => setError(caught instanceof ApiError ? caught.message : 'No pudimos diferir la compra.'),
      },
    );
  };

  return (
    <Screen dismissKeyboardOnTap>
      <View style={styles.header}>
        <Typo variant="title">Diferir en cuotas</Typo>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => router.back()} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <Card style={styles.block}>
        <Typo variant="bodyStrong" numberOfLines={1}>
          {purchase.merchant ?? purchase.description}
        </Typo>
        <Typo variant="heading" tabular>
          {formatCurrency(purchase.amount, { hidden })}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Sigue siendo un solo gasto en su fecha. Lo que cambia es cuándo se paga: una cuota por estado de cuenta.
        </Typo>
      </Card>

      <View style={styles.block}>
        <Typo variant="overline" color={colors.textSecondary}>
          NÚMERO DE CUOTAS
        </Typo>
        <View style={styles.chips}>
          {COMMON_COUNTS.map((value) => (
            <Chip
              key={value}
              label={`${value}`}
              selected={customText.trim().length === 0 && count === value}
              onPress={() => {
                setCustomText('');
                setCount(value);
              }}
            />
          ))}
        </View>
        <Input
          label="Otro número"
          value={customText}
          onChangeText={(text) => setCustomText(text.replace(/\D/g, '').slice(0, 2))}
          keyboardType="number-pad"
          placeholder="Entre 2 y 72"
          error={customText.length > 0 && !countValid ? 'Entre 2 y 72 cuotas.' : null}
        />
      </View>

      <View style={styles.block}>
        <Input
          label="Tasa de interés anual (opcional)"
          value={rateText}
          onChangeText={setRateText}
          keyboardType="decimal-pad"
          placeholder="15.5"
          error={rateValid ? null : 'Entre 0 y 100.'}
          hint="Solo informativa. Los intereses reales los registra tu estado de cuenta como movimientos de Intereses."
        />
      </View>

      {error ? (
        <Typo variant="caption" color={colors.danger} style={styles.block}>
          {error}
        </Typo>
      ) : null}

      <Button label="Diferir compra" onPress={save} disabled={!countValid || !rateValid} softDisabled loading={create.isPending} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.xl,
  },
  block: {
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
});
