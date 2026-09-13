import { useCallback, useMemo, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';
import { useToast } from '../../components/ui/Toast';
import { TypeToggle } from '../../components/quick-entry/TypeToggle';
import { useAccounts, useCategories, useCreateQuickTransaction, useUndoQuickTransaction } from '../../hooks/queries';
import { useQuickEntryStore } from '../../store/quickEntryStore';
import { evaluateExpression } from '../../utils/quickEntry/safeCalculator';
import { iconForCategory } from '../../utils/categoryIcons';
import { formatCurrency, formatDateInput, parseDateInput } from '../../utils/format';
import { AnalyticsEvent, EntryMode, ErrorReason, TransactionDirection, track } from '../../services/analytics';
import { ApiError } from '../../services/apiClient';

/**
 * §22 ("Más detalles") y §5 del planteamiento original: "NO quiero eliminar el
 * formulario completo. Quiero convertirlo en una opción secundaria."
 *
 * Esta pantalla es esa opción secundaria. Dos cosas importan de ella:
 *
 *  1. Llega RELLENA. El §22 es explícito: escribir "8 uber ayer" y tocar "Más
 *     detalles" tiene que abrir esto con tipo, monto, descripción, categoría,
 *     cuenta y fecha ya puestos. Lo que la persona escribió no se pierde nunca.
 *  2. NO crea movimientos por su cuenta. Usa el mismo `useCreateQuickTransaction`
 *     que el sheet rápido, que llama al mismo endpoint y al mismo caso de uso. Este
 *     formulario es otra manera de RELLENAR la misma petición, no un segundo
 *     sistema de transacciones.
 */
export default function NewTransactionScreen() {
  const router = useRouter();
  const toast = useToast();

  const draft = useQuickEntryStore((state) => state.draft);
  const setDraft = useQuickEntryStore((state) => state.setDraft);
  const close = useQuickEntryStore((state) => state.close);
  const returnFromFullForm = useQuickEntryStore((state) => state.returnFromFullForm);

  const { data: accounts } = useAccounts();
  const { data: categories } = useCategories();
  const create = useCreateQuickTransaction();
  const undo = useUndoQuickTransaction();

  const [dateText, setDateText] = useState(() =>
    formatDateInput(draft.occurredAt ? new Date(draft.occurredAt) : new Date()),
  );
  const [error, setError] = useState<string | null>(null);

  const amount = useMemo(() => evaluateExpression(draft.amountText)?.value ?? null, [draft.amountText]);
  const parsedDate = useMemo(() => parseDateInput(dateText), [dateText]);
  const canSave = amount !== null && amount > 0 && parsedDate !== null && !create.isPending;

  /** Cancelar devuelve al sheet con todo puesto: nada de empezar de cero. */
  const cancel = useCallback(() => {
    router.back();
    returnFromFullForm();
  }, [returnFromFullForm, router]);

  const submit = useCallback(async () => {
    if (amount === null || parsedDate === null) {
      return;
    }

    try {
      const created = await create.mutateAsync({
        amount,
        direction: draft.direction,
        description: draft.description.trim().length > 0 ? draft.description.trim() : null,
        // Igual que en el sheet: `undefined` deja categorizar al motor de reglas,
        // y solo un id explícito cuenta como decisión de la persona.
        categoryId: draft.categoryId ?? undefined,
        occurredAt: parsedDate.toISOString(),
        financialAccountId: draft.financialAccountId,
      });

      track(AnalyticsEvent.QuickEntrySaved, {
        entryMode: EntryMode.FullForm,
        transactionType:
          draft.direction === 'Income' ? TransactionDirection.Income : TransactionDirection.Expense,
        categorySource: draft.categoryId ? 'manual' : 'auto',
      });

      router.back();
      close();

      toast.show({
        message: `${formatCurrency(amount)} registrado`,
        action: {
          label: 'Deshacer',
          onPress: () => {
            track(AnalyticsEvent.QuickEntryUndo, { entryMode: EntryMode.FullForm });
            undo.mutate(created.id);
          },
        },
      });
    } catch (caught) {
      // §34: no se cierra ni se borra nada. Solo aparece el error y Reintentar.
      track(AnalyticsEvent.QuickEntryFailed, {
        entryMode: EntryMode.FullForm,
        reason: caught instanceof ApiError ? ErrorReason.ServerError : ErrorReason.NetworkError,
      });
      setError(caught instanceof ApiError ? caught.message : 'No pudimos guardar el movimiento.');
    }
  }, [amount, close, create, draft, parsedDate, router, toast, undo]);

  return (
    <Screen>
      <View style={styles.header}>
        <Typo variant="title">Nuevo movimiento</Typo>
        <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={cancel} hitSlop={12}>
          <Ionicons name="close" size={24} color={colors.textSecondary} />
        </Pressable>
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Tipo
        </Typo>
        <TypeToggle
          value={draft.direction}
          onChange={(direction) => setDraft({ direction })}
          disabled={create.isPending}
        />
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Monto
        </Typo>
        <View style={styles.amountRow}>
          <Typo style={styles.currency}>$</Typo>
          <TextInput
            value={draft.amountText}
            onChangeText={(text) => {
              setError(null);
              setDraft({ amountText: text });
            }}
            keyboardType="decimal-pad"
            placeholder="0.00"
            placeholderTextColor={colors.textSecondary}
            style={styles.amountInput}
            accessibilityLabel="Monto"
          />
        </View>
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Descripción
        </Typo>
        <TextInput
          value={draft.description}
          onChangeText={(description) => setDraft({ description })}
          placeholder="Opcional"
          placeholderTextColor={colors.textSecondary}
          style={styles.input}
          accessibilityLabel="Descripción"
        />
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Categoría
        </Typo>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
          <Pressable
            accessibilityRole="button"
            accessibilityState={{ selected: draft.categoryId === null }}
            onPress={() => setDraft({ categoryId: null })}
            style={[styles.chip, draft.categoryId === null ? styles.chipSelected : null]}
          >
            <Typo variant="caption" color={draft.categoryId === null ? colors.onPrimary : colors.text}>
              Sin categoría
            </Typo>
          </Pressable>

          {(categories ?? []).map((category) => {
            const selected = category.id === draft.categoryId;

            return (
              <Pressable
                key={category.id}
                accessibilityRole="button"
                accessibilityLabel={category.name}
                accessibilityState={{ selected }}
                onPress={() => setDraft({ categoryId: category.id })}
                style={[styles.chip, selected ? styles.chipSelected : null]}
              >
                <Ionicons
                  name={iconForCategory(category.icon)}
                  size={14}
                  color={selected ? colors.onPrimary : colors.textSecondary}
                />
                <Typo variant="caption" color={selected ? colors.onPrimary : colors.text}>
                  {category.name}
                </Typo>
              </Pressable>
            );
          })}
        </ScrollView>
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Cuenta
        </Typo>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
          <Pressable
            accessibilityRole="button"
            accessibilityState={{ selected: draft.financialAccountId === null }}
            onPress={() => setDraft({ financialAccountId: null })}
            style={[styles.chip, draft.financialAccountId === null ? styles.chipSelected : null]}
          >
            <Typo variant="caption" color={draft.financialAccountId === null ? colors.onPrimary : colors.text}>
              Efectivo
            </Typo>
          </Pressable>

          {(accounts ?? [])
            .filter((account) => account.providerCode !== 'EFECTIVO')
            .map((account) => {
              const selected = account.id === draft.financialAccountId;

              return (
                <Pressable
                  key={account.id}
                  accessibilityRole="button"
                  accessibilityLabel={account.alias}
                  accessibilityState={{ selected }}
                  onPress={() => setDraft({ financialAccountId: account.id })}
                  style={[styles.chip, selected ? styles.chipSelected : null]}
                >
                  <Typo variant="caption" color={selected ? colors.onPrimary : colors.text}>
                    {account.alias}
                  </Typo>
                </Pressable>
              );
            })}
        </ScrollView>
      </View>

      <View style={styles.field}>
        <Typo variant="caption" color={colors.textSecondary}>
          Fecha
        </Typo>
        <TextInput
          value={dateText}
          onChangeText={setDateText}
          placeholder="DD/MM/AAAA"
          placeholderTextColor={colors.textSecondary}
          style={styles.input}
          keyboardType="numbers-and-punctuation"
          accessibilityLabel="Fecha"
        />
        {parsedDate === null ? (
          <Typo variant="caption" color={colors.danger}>
            Usa el formato DD/MM/AAAA.
          </Typo>
        ) : null}
      </View>

      {error ? (
        <View style={styles.error} accessibilityLiveRegion="assertive">
          <Ionicons name="alert-circle" size={18} color={colors.danger} />
          <Typo variant="body" color={colors.danger} style={styles.errorText}>
            {error}
          </Typo>
        </View>
      ) : null}

      <View style={styles.actions}>
        <Button
          label={error ? 'Reintentar' : 'Guardar'}
          onPress={() => void submit()}
          disabled={!canSave}
          loading={create.isPending}
          loadingLabel="Guardando..."
          softDisabled
        />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.lg,
  },
  field: {
    gap: spacing.sm,
    marginBottom: spacing.lg,
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
    fontSize: typography.heading.fontSize,
    color: colors.textSecondary,
  },
  amountInput: {
    flex: 1,
    minHeight: 56,
    color: colors.text,
    fontSize: typography.heading.fontSize,
    fontWeight: '700',
  },
  input: {
    minHeight: 52,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
    color: colors.text,
    fontSize: typography.body.fontSize,
  },
  chips: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingRight: spacing.lg,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    minHeight: 44,
    paddingHorizontal: spacing.md,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
  },
  chipSelected: {
    backgroundColor: colors.primary,
  },
  error: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: 'rgba(216, 102, 91, 0.10)',
    marginBottom: spacing.lg,
  },
  errorText: {
    flex: 1,
  },
  actions: {
    marginTop: spacing.sm,
  },
});
