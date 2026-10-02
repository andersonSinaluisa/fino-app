import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Input, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useCreditCard, useDeclareStatement } from '../../hooks/queries';
import { ApiError } from '../../services/apiClient';
import { parseDateInput } from '../../utils/format';
import { parseBudgetAmount } from '../../utils/budgets';
import { cardTitle, parseLocalDate, toLocalDateString } from '../../utils/creditCards';

/** Un futuro lejano: la fecha máxima de pago sí puede ser posterior a hoy. */
const FAR_FUTURE = new Date(2100, 0, 1);

function toInput(value: string | null | undefined): string {
  const date = value ? parseLocalDate(value) : null;
  return date ? `${String(date.day).padStart(2, '0')}/${String(date.month).padStart(2, '0')}/${date.year}` : '';
}

/**
 * Registrar las cifras OFICIALES de un estado de cuenta (lo que dice el banco):
 * fecha de corte, fecha máxima de pago, total a pagar y pago mínimo. Desde ahí
 * Fino usa esas cifras en vez de las que calcula; lo pagado y lo pendiente se
 * siguen derivando de los pagos que registres.
 */
export default function DeclareStatementScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string }>();
  const { data } = useCreditCard(params.id);
  const declare = useDeclareStatement();

  const last = data?.lastStatement ?? null;
  const [closingText, setClosingText] = useState<string | null>(null);
  const [dueText, setDueText] = useState<string | null>(null);
  const [totalText, setTotalText] = useState('');
  const [minimumText, setMinimumText] = useState('');
  const [error, setError] = useState<string | null>(null);

  if (!data) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const closingValue = closingText ?? toInput(last?.closingDate);
  const dueValue = dueText ?? toInput(last?.dueDate);
  const closing = parseDateInput(closingValue);
  const due = parseDateInput(dueValue, FAR_FUTURE);
  const total = totalText.trim() === '0' ? 0 : parseBudgetAmount(totalText);
  const minimum = minimumText.trim().length > 0 ? parseBudgetAmount(minimumText) : null;
  const dueAfterClosing = closing !== null && due !== null && due.getTime() > closing.getTime();
  const canSave =
    closing !== null &&
    due !== null &&
    dueAfterClosing &&
    total !== null &&
    (minimumText.trim().length === 0 || (minimum !== null && minimum <= total));

  const save = () => {
    if (!canSave || closing === null || due === null || total === null) {
      return;
    }

    setError(null);
    declare.mutate(
      {
        id: data.card.id,
        closingDate: toLocalDateString(closing),
        dueDate: toLocalDateString(due),
        statementBalance: total,
        minimumPayment: minimum,
      },
      {
        onSuccess: () => router.back(),
        onError: (caught) => setError(caught instanceof ApiError ? caught.message : 'No pudimos guardar el estado.'),
      },
    );
  };

  return (
    <Screen dismissKeyboardOnTap>
      <View style={styles.header}>
        <View style={styles.flex}>
          <Typo variant="title">Estado de cuenta</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {cardTitle(data.card)}
          </Typo>
        </View>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => router.back()} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <Typo variant="body" color={colors.textSecondary} style={styles.intro}>
        Copia las cifras de tu estado de cuenta o de la app de tu banco. Fino las usará tal cual, sin recomendarte pagar el mínimo ni el total.
      </Typo>

      <View style={styles.form}>
        <Input
          label="Fecha de corte"
          value={closingValue}
          onChangeText={setClosingText}
          placeholder="DD/MM/AAAA"
          keyboardType="numbers-and-punctuation"
          error={closingValue.length > 0 && closing === null ? 'Una fecha válida, hoy o antes.' : null}
        />
        <Input
          label="Fecha máxima de pago"
          value={dueValue}
          onChangeText={setDueText}
          placeholder="DD/MM/AAAA"
          keyboardType="numbers-and-punctuation"
          error={closing !== null && due !== null && !dueAfterClosing ? 'Debe ser después de la fecha de corte.' : null}
        />
        <Input label="Total a pagar" value={totalText} onChangeText={setTotalText} keyboardType="decimal-pad" placeholder="420.00" />
        <Input
          label="Pago mínimo (opcional)"
          value={minimumText}
          onChangeText={setMinimumText}
          keyboardType="decimal-pad"
          placeholder="45.00"
          error={minimum !== null && total !== null && minimum > total ? 'No puede ser mayor que el total.' : null}
        />

        {error ? (
          <Typo variant="caption" color={colors.danger}>
            {error}
          </Typo>
        ) : null}

        <Button label="Guardar estado" onPress={save} disabled={!canSave} softDisabled loading={declare.isPending} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    marginBottom: spacing.lg,
  },
  intro: {
    marginBottom: spacing.xl,
  },
  form: {
    gap: spacing.lg,
  },
  flex: {
    flex: 1,
  },
});
