import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Chip, Input, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useCategories, useCreateQuickTransaction, useCreditCard } from '../../hooks/queries';
import { ApiError } from '../../services/apiClient';
import { formatDateInput, parseDateInput } from '../../utils/format';
import { parseBudgetAmount } from '../../utils/budgets';
import { MANUAL_MOVEMENT_TYPES, MOVEMENT_TYPE_HINTS, MOVEMENT_TYPE_LABELS, cardTitle, isToday } from '../../utils/creditCards';
import { createRequestId } from '../../utils/quickEntry/requestId';
import type { CardMovementType } from '../../types/api';

type ManualType = Exclude<CardMovementType, 'Payment'>;

/** Tipos a los que tiene sentido ponerles una categoría de consumo. */
const CATEGORIZED: ReadonlySet<ManualType> = new Set(['Purchase', 'Refund']);

/**
 * Registrar un movimiento de la tarjeta: compra, devolución, interés, comisión,
 * avance o ajuste. Usa el MISMO caso de uso que el registro rápido
 * (POST /quick-entry/transactions) -- no hay un segundo camino para escribir
 * movimientos. La app manda solo el TIPO: si sube o baja la deuda lo decide el
 * backend (CreditCardMovementRules), salvo en un ajuste, que puede ir en
 * cualquiera de los dos sentidos y lo elige la persona.
 */
export default function CardMovementScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string }>();
  const { data } = useCreditCard(params.id);
  const { data: categories } = useCategories();
  const create = useCreateQuickTransaction();

  const [type, setType] = useState<ManualType>('Purchase');
  const [adjustmentRaises, setAdjustmentRaises] = useState(true);
  const [amountText, setAmountText] = useState('');
  const [description, setDescription] = useState('');
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [dateText, setDateText] = useState(() => formatDateInput(new Date()));
  const [requestId] = useState(createRequestId);
  const [error, setError] = useState<string | null>(null);

  const spendingCategories = useMemo(() => (categories ?? []).filter((c) => !c.isIncome), [categories]);

  if (!data) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const amount = parseBudgetAmount(amountText);
  const date = parseDateInput(dateText);
  const canSave = amount !== null && date !== null && !create.isPending;

  const save = () => {
    if (amount === null || date === null) {
      return;
    }

    setError(null);
    create.mutate(
      {
        amount,
        financialAccountId: data.card.id,
        cardMovementType: type,
        direction: type === 'Adjustment' ? (adjustmentRaises ? 'Expense' : 'Income') : undefined,
        description: description.trim().length > 0 ? description.trim() : MOVEMENT_TYPE_LABELS[type],
        categoryId: CATEGORIZED.has(type) ? categoryId ?? undefined : undefined,
        occurredAt: isToday(date) ? null : date.toISOString(),
        clientRequestId: requestId,
      },
      {
        onSuccess: () => router.back(),
        onError: (caught) => setError(caught instanceof ApiError ? caught.message : 'No pudimos guardar el movimiento.'),
      },
    );
  };

  return (
    <Screen dismissKeyboardOnTap>
      <View style={styles.header}>
        <View style={styles.flex}>
          <Typo variant="title">Registrar movimiento</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {cardTitle(data.card)}
          </Typo>
        </View>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => router.back()} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <View style={styles.block}>
        <Typo variant="overline" color={colors.textSecondary}>
          TIPO
        </Typo>
        <View style={styles.chips}>
          {MANUAL_MOVEMENT_TYPES.map((value) => (
            <Chip key={value} label={MOVEMENT_TYPE_LABELS[value]} selected={type === value} onPress={() => setType(value)} />
          ))}
        </View>
        <Typo variant="caption" color={colors.textSecondary}>
          {MOVEMENT_TYPE_HINTS[type]}
        </Typo>
        {type === 'Adjustment' ? (
          <View style={styles.chips}>
            <Chip label="Aumenta la deuda" selected={adjustmentRaises} onPress={() => setAdjustmentRaises(true)} />
            <Chip label="Reduce la deuda" selected={!adjustmentRaises} onPress={() => setAdjustmentRaises(false)} />
          </View>
        ) : null}
      </View>

      <View style={styles.form}>
        <Input label="Monto" value={amountText} onChangeText={setAmountText} keyboardType="decimal-pad" placeholder="0.00" />
        <Input label="Descripción" value={description} onChangeText={setDescription} placeholder="Supermaxi" maxLength={120} />

        {CATEGORIZED.has(type) ? (
          <View style={styles.block}>
            <Typo variant="overline" color={colors.textSecondary}>
              CATEGORÍA (OPCIONAL)
            </Typo>
            <View style={styles.chips}>
              {spendingCategories.map((category) => (
                <Chip
                  key={category.id}
                  label={category.name}
                  selected={categoryId === category.id}
                  onPress={() => setCategoryId(categoryId === category.id ? null : category.id)}
                />
              ))}
            </View>
            <Typo variant="caption" color={colors.textSecondary}>
              {type === 'Refund'
                ? 'Usa la categoría de la compra devuelta: así se descuenta de ese gasto y de su presupuesto.'
                : 'Sin elegir, Fino la categoriza con tus reglas.'}
            </Typo>
          </View>
        ) : null}

        <Input
          label="Fecha"
          value={dateText}
          onChangeText={setDateText}
          placeholder="DD/MM/AAAA"
          keyboardType="numbers-and-punctuation"
          error={date === null ? 'Usa una fecha válida, hoy o antes.' : null}
        />

        {error ? (
          <Typo variant="caption" color={colors.danger}>
            {error}
          </Typo>
        ) : null}

        <Button label="Guardar" onPress={save} disabled={!canSave} softDisabled loading={create.isPending} />
      </View>
    </Screen>
  );
}


const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    marginBottom: spacing.xl,
  },
  block: {
    gap: spacing.sm,
    marginBottom: spacing.lg,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  form: {
    gap: spacing.lg,
  },
  flex: {
    flex: 1,
  },
});
