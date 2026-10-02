import { useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Chip, Input, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useAccounts, useCardPaymentSuggestions, useCreditCard, useLinkCardPayment, usePayCreditCard } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { ApiError } from '../../services/apiClient';
import { AnalyticsEvent, CardPaymentFlow, CardPaymentSource, track } from '../../services/analytics';
import { formatCurrency, formatDateInput, formatShortDate, parseDateInput } from '../../utils/format';
import { parseBudgetAmount } from '../../utils/budgets';
import { cardTitle, formatDueDate, isToday } from '../../utils/creditCards';
import { createRequestId } from '../../utils/quickEntry/requestId';

const EXTERNAL = '__external__';

/**
 * "Pagar tarjeta": desde qué cuenta, cuánto y cuándo. Se guarda como UN pago
 * con dos patas vinculadas (sale del banco, baja la deuda) que no son ni
 * ingreso ni gasto -- la compra ya fue el gasto. Si el débito ya está en Fino
 * (importado del banco), se vincula en vez de crear otro.
 */
export default function PayCreditCardScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ id: string }>();
  const id = params.id;
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data } = useCreditCard(id);
  const { data: accounts } = useAccounts();
  const suggestions = useCardPaymentSuggestions(id);
  const pay = usePayCreditCard();
  const link = useLinkCardPayment();

  const card = data?.card;
  const moneyAccounts = useMemo(() => (accounts ?? []).filter((a) => !a.isLiability && !a.isArchived), [accounts]);

  const [amountText, setAmountText] = useState<string | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [dateText, setDateText] = useState(() => formatDateInput(new Date()));
  const [requestId] = useState(createRequestId);
  const [error, setError] = useState<string | null>(null);

  if (!card) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const next = card.nextPayment;
  const effectiveAmountText = amountText ?? (next ? next.amount.toFixed(2) : '');
  const amount = parseBudgetAmount(effectiveAmountText);
  const selectedSource = source ?? moneyAccounts[0]?.id ?? EXTERNAL;
  const paidAt = parseDateInput(dateText);
  const canSave = amount !== null && paidAt !== null && !pay.isPending;

  const presets: { label: string; value: number }[] = [];
  if (next) {
    presets.push({ label: `Próximo pago ${formatCurrency(next.amount, { hidden })}`, value: next.amount });
    if (next.minimumPayment !== null && next.minimumPayment > 0) {
      presets.push({ label: `Mínimo ${formatCurrency(next.minimumPayment, { hidden })}`, value: next.minimumPayment });
    }
  }
  if (card.currentDebt > 0 && (!next || card.currentDebt !== next.amount)) {
    presets.push({ label: `Deuda total ${formatCurrency(card.currentDebt, { hidden })}`, value: card.currentDebt });
  }

  const submit = () => {
    if (amount === null || paidAt === null) {
      return;
    }

    setError(null);
    const external = selectedSource === EXTERNAL;
    pay.mutate(
      {
        id: card.id,
        amount,
        sourceAccountId: external ? null : selectedSource,
        // Hoy = "ahora" (lo pone el servidor): así el pago queda después de un
        // saldo verificado que se haya registrado hoy más temprano.
        paidAt: isToday(paidAt) ? null : paidAt.toISOString(),
        clientRequestId: requestId,
      },
      {
        onSuccess: () => {
          track(AnalyticsEvent.CreditCardPaymentRegistered, {
            paymentSource: external ? CardPaymentSource.External : CardPaymentSource.Account,
            flow: CardPaymentFlow.Manual,
          });
          router.back();
        },
        onError: (caught) => setError(caught instanceof ApiError ? caught.message : 'No pudimos registrar el pago.'),
      },
    );
  };

  const confirmLink = (bankTransactionId: string, cardTransactionId: string | null, label: string) => {
    Alert.alert('¿Es este tu pago?', `${label}\n\nDejará de contar como gasto: el gasto ya fueron las compras de la tarjeta.`, [
      { text: 'No', style: 'cancel' },
      {
        text: 'Sí, es el pago',
        onPress: () =>
          link.mutate(
            { id: card.id, bankTransactionId, cardTransactionId },
            {
              onSuccess: () => {
                track(AnalyticsEvent.CreditCardPaymentRegistered, {
                  paymentSource: CardPaymentSource.Account,
                  flow: CardPaymentFlow.Link,
                });
                router.back();
              },
              onError: (caught) => Alert.alert('No pudimos vincularlo', caught.message),
            },
          ),
      },
    ]);
  };

  return (
    <Screen dismissKeyboardOnTap>
      <View style={styles.header}>
        <View style={styles.flex}>
          <Typo variant="title">Pagar tarjeta</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {cardTitle(card)}
          </Typo>
        </View>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => router.back()} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      {suggestions.data && suggestions.data.length > 0 ? (
        <View style={styles.block}>
          <Typo variant="overline" color={colors.textSecondary}>
            ¿YA PAGASTE DESDE TU BANCO?
          </Typo>
          <View style={styles.listCard}>
            {suggestions.data.map((suggestion, index) => {
              const label = `${suggestion.bankAccountAlias} · ${formatShortDate(suggestion.date)} · ${formatCurrency(suggestion.amount, { hidden })}`;
              return (
                <View key={suggestion.bankTransactionId}>
                  {index > 0 ? <View style={styles.divider} /> : null}
                  <View style={styles.suggestionRow}>
                    <View style={styles.flex}>
                      <Typo variant="bodyStrong" numberOfLines={1}>
                        {suggestion.description}
                      </Typo>
                      <Typo variant="caption" color={colors.textSecondary}>
                        {label}
                      </Typo>
                      <Typo variant="caption" color={colors.textSecondary}>
                        {suggestion.reason === 'matches_card_payment'
                          ? 'Mismo monto que un pago que ya está en la tarjeta.'
                          : 'El texto del banco parece un pago de tarjeta.'}
                      </Typo>
                    </View>
                    <Button
                      label="Es este"
                      variant="secondary"
                      compact
                      fullWidth={false}
                      loading={link.isPending}
                      onPress={() => confirmLink(suggestion.bankTransactionId, suggestion.cardTransactionId, label)}
                    />
                  </View>
                </View>
              );
            })}
          </View>
        </View>
      ) : null}

      <View style={styles.block}>
        <Input
          label="Monto"
          value={effectiveAmountText}
          onChangeText={setAmountText}
          keyboardType="decimal-pad"
          placeholder="0.00"
        />
        {presets.length > 0 ? (
          <View style={styles.chips}>
            {presets.map((preset) => (
              <Chip
                key={preset.label}
                label={preset.label}
                selected={amount === preset.value}
                onPress={() => setAmountText(preset.value.toFixed(2))}
              />
            ))}
          </View>
        ) : null}
        {next ? (
          <Typo variant="caption" color={colors.textSecondary}>
            Vence el {formatDueDate(next.dueDate)}. Fino no te recomienda cuánto pagar: registra lo que de verdad pagaste.
          </Typo>
        ) : null}
      </View>

      <View style={styles.block}>
        <Typo variant="overline" color={colors.textSecondary}>
          DESDE
        </Typo>
        <View style={styles.chips}>
          {moneyAccounts.map((account) => (
            <Chip key={account.id} label={account.alias} selected={selectedSource === account.id} onPress={() => setSource(account.id)} />
          ))}
          <Chip label="Otra cuenta (fuera de Fino)" selected={selectedSource === EXTERNAL} onPress={() => setSource(EXTERNAL)} />
        </View>
        <Typo variant="caption" color={colors.textSecondary}>
          {selectedSource === EXTERNAL
            ? 'Solo baja la deuda de la tarjeta; ninguna de tus cuentas cambia.'
            : 'Sale de esa cuenta y baja la deuda. No cuenta como gasto: el gasto fueron las compras.'}
        </Typo>
      </View>

      <View style={styles.block}>
        <Input
          label="Fecha"
          value={dateText}
          onChangeText={setDateText}
          placeholder="DD/MM/AAAA"
          keyboardType="numbers-and-punctuation"
          error={paidAt === null ? 'Usa una fecha válida, hoy o antes.' : null}
        />
      </View>

      {error ? (
        <Typo variant="caption" color={colors.danger} style={styles.block}>
          {error}
        </Typo>
      ) : null}

      <View style={styles.block}>
        <Button label="Registrar pago" onPress={submit} disabled={!canSave} softDisabled loading={pay.isPending} />
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
    marginBottom: spacing.xl,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  listCard: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
  },
  suggestionRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  flex: {
    flex: 1,
  },
});
