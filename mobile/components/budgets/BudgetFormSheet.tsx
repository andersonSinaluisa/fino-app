import { useEffect, useMemo, useState } from 'react';
import { Modal, Pressable, ScrollView, StyleSheet, Switch, TextInput, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Button, Chip, SelectSheet, Typo, type SelectSheetOption } from '../ui';
import { useBudgetPreview, useCategories, useCreateBudget, useUpdateBudget } from '../../hooks/queries';
import { ApiError } from '../../services/apiClient';
import { AnalyticsEvent, BudgetFlow, BudgetPeriodValue, track } from '../../services/analytics';
import { formatCurrency } from '../../utils/format';
import { PERIOD_LABELS, PRIORITY_LABELS, parseBudgetAmount, parseDayInput } from '../../utils/budgets';
import type {
  Budget,
  BudgetDetail,
  BudgetPeriod,
  BudgetPreviewRequest,
  BudgetPriority,
} from '../../types/api';

interface BudgetFormSheetProps {
  visible: boolean;
  /** Presente => editar; ausente => crear. */
  budget?: Budget | null;
  /** Prellenar la categoría al crear desde otra pantalla. */
  initialCategoryId?: string | null;
  hidden: boolean;
  onClose: () => void;
  onSaved?: (detail: BudgetDetail) => void;
}

const PERIODS: BudgetPeriod[] = ['Monthly', 'Weekly', 'Biweekly', 'Custom'];
const PRIORITIES: BudgetPriority[] = ['Essential', 'Important', 'Flexible'];
const GENERAL = '__general__';
const PREVIEW_DEBOUNCE_MS = 350;

const PERIOD_ANALYTICS: Record<BudgetPeriod, string> = {
  Weekly: BudgetPeriodValue.Weekly,
  Biweekly: BudgetPeriodValue.Biweekly,
  Monthly: BudgetPeriodValue.Monthly,
  Custom: BudgetPeriodValue.Custom,
};

/**
 * Nuevo / editar presupuesto. Un solo paso: categoría, monto, período y la
 * decisión que de verdad importa -- ¿solo controlar el gasto, o además
 * reservar el dinero?
 *
 * El bloque "Disponible después" NO es una fórmula de la app: pide al backend
 * POST /budgets/preview, que pasa por el mismo CommittedMoneyCalculator que el
 * Comprometido real (incluida la deduplicación con próximos pagos).
 */
export function BudgetFormSheet(props: BudgetFormSheetProps) {
  // El formulario se monta al abrir y se desmonta al cerrar: así arranca siempre
  // con los valores del presupuesto (o vacío) sin un efecto que "resetee".
  return (
    <Modal visible={props.visible} transparent animationType="slide" onRequestClose={props.onClose}>
      {props.visible ? <BudgetForm {...props} /> : null}
    </Modal>
  );
}

function BudgetForm({ budget, initialCategoryId, hidden, onClose, onSaved }: BudgetFormSheetProps) {
  const isEdit = Boolean(budget);
  const { data: categories } = useCategories();
  const createBudget = useCreateBudget();
  const updateBudget = useUpdateBudget();

  const [categoryId, setCategoryId] = useState<string | null>(budget ? budget.categoryId : (initialCategoryId ?? null));
  const [amountText, setAmountText] = useState(budget ? String(budget.amount) : '');
  const [name, setName] = useState(budget?.name ?? '');
  const [period, setPeriod] = useState<BudgetPeriod>(budget?.period ?? 'Monthly');
  const [priority, setPriority] = useState<BudgetPriority>(budget?.priority ?? 'Important');
  const [reserveFunds, setReserveFunds] = useState(budget?.reserveFunds ?? false);
  const [startText, setStartText] = useState('');
  const [endText, setEndText] = useState('');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const expenseCategories = useMemo(() => (categories ?? []).filter((c) => !c.isIncome), [categories]);
  const selectedCategory = expenseCategories.find((c) => c.id === categoryId) ?? null;

  const categoryOptions: SelectSheetOption<string>[] = useMemo(
    () => [
      { value: GENERAL, label: 'Sin categoría · todos los demás gastos' },
      ...expenseCategories.map((c) => ({ value: c.id, label: c.name })),
    ],
    [expenseCategories],
  );

  const amount = parseBudgetAmount(amountText);
  const customStart = period === 'Custom' && !isEdit ? parseDayInput(startText) : null;
  const customEnd = period === 'Custom' && !isEdit ? parseDayInput(endText) : null;
  const customValid = period !== 'Custom' || isEdit || (customStart !== null && customEnd !== null && customEnd >= customStart);

  // Debounce: no pedir una previsualización por cada tecla. La petición se
  // describe como texto estable para poder compararla entre renders.
  const previewWanted = reserveFunds || Boolean(budget?.reserveFunds);
  const requestKey =
    amount !== null && customValid && previewWanted
      ? JSON.stringify({
          amount,
          categoryId,
          period: budget ? budget.period : period,
          startDate: budget ? budget.startDate : customStart,
          endDate: budget ? budget.endDate : customEnd,
          reserveFunds,
          budgetId: budget?.id ?? null,
        } satisfies BudgetPreviewRequest)
      : null;
  const [debouncedKey, setDebouncedKey] = useState<string | null>(null);
  useEffect(() => {
    if (requestKey === null) {
      return undefined;
    }
    const timer = setTimeout(() => setDebouncedKey(requestKey), PREVIEW_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [requestKey]);
  const debounced: BudgetPreviewRequest | null =
    requestKey !== null && debouncedKey !== null ? (JSON.parse(debouncedKey) as BudgetPreviewRequest) : null;

  const preview = useBudgetPreview(debounced);

  const submitting = createBudget.isPending || updateBudget.isPending;
  const canSubmit = amount !== null && customValid && !submitting;

  async function handleSubmit() {
    if (amount === null) {
      return;
    }
    setError(null);

    try {
      let detail: BudgetDetail;
      if (budget) {
        detail = await updateBudget.mutateAsync({
          id: budget.id,
          amount,
          categoryId,
          name: name.trim() || null,
          endDate: budget.endDate,
          reserveFunds,
          priority,
          isActive: budget.isActive,
        });
        track(AnalyticsEvent.BudgetUpdated, { reserves: reserveFunds, paused: !budget.isActive });
        if (reserveFunds && !budget.reserveFunds) {
          track(AnalyticsEvent.BudgetReserveEnabled, { flow: BudgetFlow.Edit });
        }
      } else {
        detail = await createBudget.mutateAsync({
          amount,
          categoryId,
          name: name.trim() || null,
          period,
          startDate: customStart,
          endDate: customEnd,
          reserveFunds,
          priority,
        });
        track(AnalyticsEvent.BudgetCreated, {
          period: PERIOD_ANALYTICS[period],
          reserves: reserveFunds,
          hasCategory: categoryId !== null,
          priority: priority.toLowerCase(),
        });
        if (reserveFunds) {
          track(AnalyticsEvent.BudgetReserveEnabled, { flow: BudgetFlow.Create });
        }
      }
      onSaved?.(detail);
      onClose();
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? (Object.values(caught.fieldErrors).find(Boolean) ?? caught.message)
          : 'No pudimos guardar el presupuesto. Intenta de nuevo.',
      );
    }
  }

  const money = (value: number, signed = false) => formatCurrency(value, { hidden, signed });

  return (
    <>
      <Pressable style={styles.backdrop} onPress={onClose} accessibilityRole="button" accessibilityLabel="Cerrar" />

      <View style={styles.sheet}>
        <View style={styles.handle} />
        <Typo variant="bodyStrong" style={styles.title}>
          {isEdit ? 'Editar presupuesto' : 'Nuevo presupuesto'}
        </Typo>

        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          <Typo variant="caption" color={colors.textSecondary} style={styles.label}>
            Categoría
          </Typo>
          <Pressable
            onPress={() => setPickerOpen(true)}
            accessibilityRole="button"
            accessibilityLabel={`Categoría: ${selectedCategory?.name ?? 'sin categoría'}. Cambiar`}
            style={styles.selector}
          >
            <Typo variant="body" style={styles.flex} numberOfLines={1}>
              {selectedCategory?.name ?? 'Sin categoría · todos los demás gastos'}
            </Typo>
            <Ionicons name="chevron-down" size={16} color={colors.textSecondary} />
          </Pressable>
          {!selectedCategory ? (
            <Typo variant="caption" color={colors.textSecondary} style={styles.help}>
              Cuenta los gastos que no tengan su propio presupuesto.
            </Typo>
          ) : null}

          <Typo variant="caption" color={colors.textSecondary} style={styles.label}>
            Monto
          </Typo>
          <TextInput
            value={amountText}
            onChangeText={setAmountText}
            placeholder="$300"
            placeholderTextColor={colors.textSecondary}
            keyboardType="decimal-pad"
            style={styles.input}
            accessibilityLabel="Monto del presupuesto"
          />

          <Typo variant="caption" color={colors.textSecondary} style={styles.label}>
            Período
          </Typo>
          {isEdit ? (
            <Typo variant="body" color={colors.textSecondary} style={styles.help}>
              {PERIOD_LABELS[period]} · para cambiar el período crea un presupuesto nuevo, así tu historial no cambia.
            </Typo>
          ) : (
            <View style={styles.chips}>
              {PERIODS.map((value) => (
                <Chip key={value} label={PERIOD_LABELS[value]} selected={period === value} onPress={() => setPeriod(value)} />
              ))}
            </View>
          )}

          {period === 'Custom' && !isEdit ? (
            <View style={styles.dates}>
              <TextInput
                value={startText}
                onChangeText={setStartText}
                placeholder="Desde DD/MM/AAAA"
                placeholderTextColor={colors.textSecondary}
                style={[styles.input, styles.flex]}
                accessibilityLabel="Fecha de inicio"
              />
              <TextInput
                value={endText}
                onChangeText={setEndText}
                placeholder="Hasta DD/MM/AAAA"
                placeholderTextColor={colors.textSecondary}
                style={[styles.input, styles.flex]}
                accessibilityLabel="Fecha de fin"
              />
            </View>
          ) : null}

          {/* "Reservar este dinero": la decisión que cambia el Disponible, así que
              va como tarjeta grande y tocable entera -- no como un switch perdido
              al final. El estado se dice con texto e ícono, no solo con color. */}
          <Pressable
            onPress={() => setReserveFunds((value) => !value)}
            accessibilityRole="switch"
            accessibilityState={{ checked: reserveFunds }}
            accessibilityLabel="Reservar este dinero"
            accessibilityHint={reserveFunds ? 'Toca para que solo controle el gasto' : 'Toca para apartar este dinero de tu Disponible'}
            style={({ pressed }) => [
              styles.reserveCard,
              reserveFunds ? styles.reserveCardOn : null,
              pressed ? styles.reservePressed : null,
            ]}
          >
            <View style={[styles.reserveIcon, reserveFunds ? styles.reserveIconOn : null]}>
              <Ionicons
                name={reserveFunds ? 'lock-closed' : 'lock-open-outline'}
                size={20}
                color={reserveFunds ? colors.onAccent : colors.text}
              />
            </View>
            <View style={styles.flex}>
              <View style={styles.reserveTitleRow}>
                <Typo variant="bodyStrong">Reservar este dinero</Typo>
                <View style={[styles.reserveState, reserveFunds ? styles.reserveStateOn : null]}>
                  <Typo variant="overline" color={reserveFunds ? colors.onPrimary : colors.textSecondary}>
                    {reserveFunds ? 'SÍ' : 'NO'}
                  </Typo>
                </View>
              </View>
              <Typo variant="caption" color={reserveFunds ? colors.text : colors.textSecondary}>
                {reserveFunds
                  ? 'Este dinero se tendrá en cuenta para calcular cuánto tienes realmente disponible.'
                  : 'Solo controla cuánto gastas. Actívalo para apartar este dinero de tu Disponible (alquiler, servicios…).'}
              </Typo>
            </View>
            <Switch
              value={reserveFunds}
              onValueChange={setReserveFunds}
              trackColor={{ true: colors.primary, false: colors.borderStrong }}
              thumbColor={reserveFunds ? colors.accent : colors.surface}
              ios_backgroundColor={colors.borderStrong}
              accessibilityElementsHidden
              importantForAccessibility="no"
            />
          </Pressable>

          <Typo variant="caption" color={colors.textSecondary} style={styles.label}>
            Nombre (opcional)
          </Typo>
          <TextInput
            value={name}
            onChangeText={setName}
            placeholder={selectedCategory?.name ?? 'Presupuesto general'}
            placeholderTextColor={colors.textSecondary}
            style={styles.input}
            maxLength={60}
          />

          <Typo variant="caption" color={colors.textSecondary} style={styles.label}>
            Prioridad
          </Typo>
          <View style={styles.chips}>
            {PRIORITIES.map((value) => (
              <Chip key={value} label={PRIORITY_LABELS[value]} selected={priority === value} onPress={() => setPriority(value)} />
            ))}
          </View>

          {previewWanted && amount !== null && customValid ? (
            <View style={styles.preview} accessibilityLiveRegion="polite">
              {preview.data ? (
                <>
                  <PreviewRow label="Tu dinero" value={money(preview.data.currentMoney)} />
                  <PreviewRow label="Comprometido actual" value={money(preview.data.committedNow)} />
                  <PreviewRow label="Este presupuesto" value={money(preview.data.budgetContribution, true)} />
                  <View style={styles.divider} />
                  <PreviewRow label="Disponible después" value={money(preview.data.availableAfter)} strong />
                  {preview.data.alreadySpent > 0 ? (
                    <Typo variant="caption" color={colors.textSecondary}>
                      Ya gastaste {money(preview.data.alreadySpent)} de esto en el período; solo se reserva lo que falta.
                    </Typo>
                  ) : null}
                  {preview.data.overcommittedAfter > 0 ? (
                    <Typo variant="caption" color={colors.danger}>
                      Tendrías {money(preview.data.overcommittedAfter)} más comprometidos de lo que tienes.
                    </Typo>
                  ) : null}
                </>
              ) : (
                <Typo variant="caption" color={colors.textSecondary}>
                  {preview.isError ? 'No pudimos calcular el impacto ahora.' : 'Calculando tu Disponible…'}
                </Typo>
              )}
            </View>
          ) : null}

          {error ? (
            <Typo variant="caption" color={colors.danger} style={styles.error}>
              {error}
            </Typo>
          ) : null}
        </ScrollView>

        <Button
          label={isEdit ? 'Guardar cambios' : 'Crear presupuesto'}
          onPress={() => void handleSubmit()}
          disabled={!canSubmit}
          loading={submitting}
          loadingLabel="Guardando..."
        />
      </View>

      <SelectSheet
        visible={pickerOpen}
        title="Categoría"
        options={categoryOptions}
        selectedValue={categoryId ?? GENERAL}
        onSelect={(value) => {
          setCategoryId(value === GENERAL ? null : value);
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
      />
    </>
  );
}

function PreviewRow({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.previewRow}>
      <Typo variant="caption" color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular>
        {value}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: colors.overlay,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.xxl,
    maxHeight: '90%',
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.md,
  },
  title: {
    marginBottom: spacing.md,
  },
  body: {
    paddingBottom: spacing.lg,
  },
  label: {
    marginBottom: spacing.sm,
  },
  help: {
    marginTop: -spacing.sm,
    marginBottom: spacing.lg,
  },
  flex: {
    flex: 1,
  },
  selector: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    marginBottom: spacing.lg,
  },
  input: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
    color: colors.text,
    fontSize: typography.body.fontSize,
    marginBottom: spacing.lg,
  },
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginBottom: spacing.lg,
  },
  dates: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  reserveCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 2,
    borderColor: colors.borderStrong,
    padding: spacing.lg,
    marginBottom: spacing.lg,
  },
  reserveCardOn: {
    borderColor: colors.primary,
    backgroundColor: 'rgba(199, 243, 107, 0.28)',
  },
  reservePressed: {
    opacity: 0.85,
  },
  reserveIcon: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  reserveIconOn: {
    backgroundColor: colors.accent,
  },
  reserveTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: 2,
  },
  reserveState: {
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
  },
  reserveStateOn: {
    backgroundColor: colors.primary,
  },
  preview: {
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.lg,
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  previewRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.xs,
  },
  error: {
    marginBottom: spacing.sm,
  },
});
