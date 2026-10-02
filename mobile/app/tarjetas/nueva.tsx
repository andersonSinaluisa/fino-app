import { useMemo, useState, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Chip, Input, Screen, SelectSheet, SkeletonCard, Typo, type SelectSheetOption } from '../../components/ui';
import { ReserveToggle } from '../../components/creditCards/ReserveToggle';
import { useCreateCreditCard, useCreditCard, useProviders, useUpdateCreditCard } from '../../hooks/queries';
import { ApiError } from '../../services/apiClient';
import { AnalyticsEvent, BudgetFlow, track } from '../../services/analytics';
import { parseBudgetAmount } from '../../utils/budgets';
import { DAY_OPTIONS, NETWORK_LABELS } from '../../utils/creditCards';
import type { CardNetwork, CreditCardSummary } from '../../types/api';

const NETWORKS: CardNetwork[] = ['Visa', 'Mastercard', 'AmericanExpress', 'Diners', 'Other'];

type Picker = 'bank' | 'closing' | 'due' | null;

/**
 * Nueva tarjeta (y editar con ?id=). Lo mínimo para entender la deuda: nombre,
 * banco, últimos 4, cupo, día de corte, día máximo de pago y si reservar el
 * próximo pago. NUNCA pide el número completo, el CVV ni el PIN -- el campo de
 * dígitos acepta exactamente cuatro.
 */
export default function CreditCardFormScreen() {
  const params = useLocalSearchParams<{ id?: string; providerCode?: string }>();
  const editingId = params.id || undefined;
  const existing = useCreditCard(editingId);

  // Editar: el formulario se monta ya con los datos de la tarjeta (estado
  // inicial), en vez de copiarlos en un efecto después de renderizar.
  if (editingId && !existing.data) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  return <CreditCardForm card={existing.data?.card ?? null} initialProviderCode={params.providerCode ?? null} />;
}

function CreditCardForm({ card, initialProviderCode }: { card: CreditCardSummary | null; initialProviderCode: string | null }) {
  const router = useRouter();
  const editingId = card?.id;
  const { data: providers } = useProviders();
  const create = useCreateCreditCard();
  const update = useUpdateCreditCard();

  const [name, setName] = useState(card?.name ?? '');
  const [providerCode, setProviderCode] = useState<string | null>(card?.providerCode ?? initialProviderCode);
  const [network, setNetwork] = useState<CardNetwork>(card ? card.network ?? 'Other' : 'Visa');
  const [lastFour, setLastFour] = useState(card?.lastFour ?? '');
  const [limitText, setLimitText] = useState(card?.creditLimit != null ? card.creditLimit.toFixed(2) : '');
  const [closingDay, setClosingDay] = useState<number | null>(card?.closingDay ?? null);
  const [dueDay, setDueDay] = useState<number | null>(card?.paymentDueDay ?? null);
  const [debtText, setDebtText] = useState('');
  const [autoReserve, setAutoReserve] = useState(card ? (card.needsSetup ? true : card.autoReserve) : true);
  const [picker, setPicker] = useState<Picker>(null);
  const [error, setError] = useState<string | null>(null);

  const banks = useMemo(() => (providers ?? []).filter((p) => p.kind === 'Bank'), [providers]);
  const bank = banks.find((p) => p.code === providerCode) ?? null;
  const limit = parseBudgetAmount(limitText);
  const debt = debtText.trim().length > 0 ? parseBudgetAmount(debtText) : null;
  const lastFourValid = lastFour.length === 0 || /^\d{4}$/.test(lastFour);
  const sameDays = closingDay !== null && closingDay === dueDay;
  const canSave =
    name.trim().length > 0 &&
    (editingId !== undefined || providerCode !== null) &&
    limit !== null &&
    closingDay !== null &&
    dueDay !== null &&
    !sameDays &&
    lastFourValid &&
    (debtText.trim().length === 0 || debt !== null);

  const bankOptions: SelectSheetOption<string>[] = banks.map((p) => ({ value: p.code, label: p.name }));
  const dayOptions: SelectSheetOption<string>[] = DAY_OPTIONS.map((day) => ({ value: String(day), label: `Día ${day}` }));

  const save = () => {
    if (!canSave || limit === null || closingDay === null || dueDay === null) {
      return;
    }

    setError(null);
    const onError = (caught: unknown) =>
      setError(caught instanceof ApiError ? caught.message : 'No pudimos guardar la tarjeta. Inténtalo de nuevo.');

    if (editingId && card) {
      update.mutate(
        {
          id: editingId,
          name: name.trim(),
          lastFour: lastFour.length > 0 ? lastFour : null,
          creditLimit: limit,
          closingDay,
          paymentDueDay: dueDay,
          autoReserve,
          network,
        },
        {
          onSuccess: () => {
            if (autoReserve && !card.autoReserve) {
              track(AnalyticsEvent.CreditCardAutoReserveEnabled, { flow: BudgetFlow.Edit });
            }
            router.back();
          },
          onError,
        },
      );
      return;
    }

    create.mutate(
      {
        name: name.trim(),
        providerCode: providerCode!,
        lastFour: lastFour.length > 0 ? lastFour : null,
        creditLimit: limit,
        closingDay,
        paymentDueDay: dueDay,
        autoReserve,
        network,
        currency: 'USD',
        currentDebt: debt,
      },
      {
        onSuccess: (detail) => {
          track(AnalyticsEvent.CreditCardCreated, { autoReserve, flow: BudgetFlow.Create });
          if (autoReserve) {
            track(AnalyticsEvent.CreditCardAutoReserveEnabled, { flow: BudgetFlow.Create });
          }
          router.replace({ pathname: '/tarjetas/[id]', params: { id: detail.card.id } });
        },
        onError,
      },
    );
  };

  const title = editingId ? (card?.needsSetup ? 'Completa tu tarjeta' : 'Editar tarjeta') : 'Nueva tarjeta';

  return (
    <Screen dismissKeyboardOnTap>
      <View style={styles.header}>
        <Typo variant="title">{title}</Typo>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => router.back()} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <View style={styles.form}>
        <Input label="Nombre" value={name} onChangeText={setName} placeholder="Visa Pichincha" maxLength={80} />

        {!editingId ? (
          <Field label="Banco">
            <Pressable
              onPress={() => setPicker('bank')}
              accessibilityRole="button"
              accessibilityLabel="Elegir banco"
              style={styles.select}
            >
              <Typo variant="body" color={bank ? colors.text : colors.textSecondary}>
                {bank?.name ?? 'Elige el banco que emitió la tarjeta'}
              </Typo>
              <Ionicons name="chevron-down" size={16} color={colors.textSecondary} />
            </Pressable>
          </Field>
        ) : null}

        <Field label="Marca">
          <View style={styles.chips}>
            {NETWORKS.map((value) => (
              <Chip key={value} label={NETWORK_LABELS[value]} selected={network === value} onPress={() => setNetwork(value)} />
            ))}
          </View>
        </Field>

        <Input
          label="Últimos 4 dígitos"
          value={lastFour}
          onChangeText={(text) => setLastFour(text.replace(/\D/g, '').slice(0, 4))}
          keyboardType="number-pad"
          maxLength={4}
          placeholder="4582"
          error={lastFourValid ? null : 'Escribe exactamente 4 dígitos.'}
          hint="Solo los últimos 4. Fino nunca pide ni guarda el número completo, el CVV ni el PIN."
        />

        <Input
          label="Cupo"
          value={limitText}
          onChangeText={setLimitText}
          keyboardType="decimal-pad"
          placeholder="2000.00"
          hint="Tu cupo es crédito, no dinero: nunca se suma a Tu dinero ni a tu Disponible."
        />

        <View style={styles.days}>
          <View style={styles.flex}>
            <Field label="Día de corte">
              <Pressable onPress={() => setPicker('closing')} accessibilityRole="button" style={styles.select}>
                <Typo variant="body" color={closingDay ? colors.text : colors.textSecondary}>
                  {closingDay ? `Día ${closingDay}` : 'Elegir'}
                </Typo>
                <Ionicons name="chevron-down" size={16} color={colors.textSecondary} />
              </Pressable>
            </Field>
          </View>
          <View style={styles.flex}>
            <Field label="Día máximo de pago">
              <Pressable onPress={() => setPicker('due')} accessibilityRole="button" style={styles.select}>
                <Typo variant="body" color={dueDay ? colors.text : colors.textSecondary}>
                  {dueDay ? `Día ${dueDay}` : 'Elegir'}
                </Typo>
                <Ionicons name="chevron-down" size={16} color={colors.textSecondary} />
              </Pressable>
            </Field>
          </View>
        </View>
        {sameDays ? (
          <Typo variant="caption" color={colors.danger}>
            El día de pago no puede ser el mismo día del corte.
          </Typo>
        ) : (
          <Typo variant="caption" color={colors.textSecondary}>
            Si tu corte es el 31 y el mes no lo tiene, Fino usa el último día del mes.
          </Typo>
        )}

        {!editingId ? (
          <Input
            label="Deuda actual (opcional)"
            value={debtText}
            onChangeText={setDebtText}
            keyboardType="decimal-pad"
            placeholder="0.00"
            hint="Lo que tu banco dice que debes hoy. Si lo dejas vacío, Fino la arma con tus movimientos."
          />
        ) : null}

        <ReserveToggle value={autoReserve} onChange={setAutoReserve} />

        {error ? (
          <Typo variant="caption" color={colors.danger}>
            {error}
          </Typo>
        ) : null}

        <Button
          label={editingId ? 'Guardar cambios' : 'Guardar tarjeta'}
          onPress={save}
          disabled={!canSave}
          softDisabled
          loading={create.isPending || update.isPending}
        />
      </View>

      <SelectSheet
        visible={picker === 'bank'}
        title="Banco emisor"
        options={bankOptions}
        selectedValue={providerCode ?? ''}
        onSelect={(value) => {
          setProviderCode(value);
          setPicker(null);
        }}
        onClose={() => setPicker(null)}
      />
      <SelectSheet
        visible={picker === 'closing' || picker === 'due'}
        title={picker === 'closing' ? 'Día de corte' : 'Día máximo de pago'}
        options={dayOptions}
        selectedValue={String((picker === 'closing' ? closingDay : dueDay) ?? '')}
        onSelect={(value) => {
          if (picker === 'closing') {
            setClosingDay(Number(value));
          } else {
            setDueDay(Number(value));
          }
          setPicker(null);
        }}
        onClose={() => setPicker(null)}
      />
    </Screen>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <View style={styles.field}>
      <Typo variant="overline" color={colors.textSecondary}>
        {label.toUpperCase()}
      </Typo>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.xl,
  },
  form: {
    gap: spacing.lg,
  },
  field: {
    gap: spacing.sm,
  },
  select: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md + 2,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  days: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
});
