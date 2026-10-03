import { useMemo, useRef, useState } from 'react';
import { Alert, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { SafeAreaView } from 'react-native-safe-area-context';
import { colors, radius, spacing, typography } from '../../../theme';
import { Button, EmptyState, KeyboardAwareScrollView, SelectSheet, SkeletonCard, Typo, type SelectSheetOption } from '../../../components/ui';
import { useCategories, useRemoveSplits, useReplaceSplits, useTransaction } from '../../../hooks/queries';
import { ApiError } from '../../../services/apiClient';
import { AnalyticsEvent, TransactionDirection, track } from '../../../services/analytics';
import { formatCurrency } from '../../../utils/format';
import {
  centsToInput,
  draftToRequest,
  fromCents,
  parseCents,
  partsBucket,
  summarizeSplitDraft,
  toCents,
  type SplitDraftLine,
} from '../../../utils/splits';
import type { TransactionDetail } from '../../../types/api';

const NONE = '__none__';

let keySeed = 0;
const nextKey = () => `line-${++keySeed}`;

/**
 * Dividir movimiento. El movimiento bancario no cambia: aquí solo se decide
 * cómo se reparte entre categorías. El resumen de abajo (Total / Distribuido /
 * Falta) se recalcula con cada tecla, en centavos enteros, y el botón de
 * guardar solo se habilita cuando la división cuadra exacto -- la misma regla
 * que valida el backend.
 */
export default function SplitTransactionScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data, isLoading, isError } = useTransaction(id ?? '');

  if (isError) {
    return (
      <SafeAreaView style={styles.root}>
        <EmptyState icon="alert-circle-outline" title="No encontramos este movimiento" />
      </SafeAreaView>
    );
  }

  if (isLoading || !data) {
    return (
      <SafeAreaView style={styles.root}>
        <View style={styles.padded}>
          <SkeletonCard />
        </View>
      </SafeAreaView>
    );
  }

  // Se monta cuando ya hay datos, así el borrador arranca con la división
  // guardada (o con la categoría actual) sin un efecto que lo "resetee".
  return <SplitEditor transaction={data} />;
}

function initialLines(transaction: TransactionDetail): SplitDraftLine[] {
  if (transaction.isSplit && transaction.splits.length > 0) {
    return transaction.splits.map((split) => ({
      key: nextKey(),
      categoryId: split.categoryId,
      amountText: centsToInput(toCents(split.amount)),
      note: split.note ?? '',
    }));
  }

  // Primera parte: la categoría que ya tenía, sin monto -- la persona escribe
  // cuánto le corresponde y ve al instante cuánto falta.
  return [{ key: nextKey(), categoryId: transaction.categoryId, amountText: '', note: '' }];
}

function SplitEditor({ transaction }: { transaction: TransactionDetail }) {
  const router = useRouter();
  const { data: categories } = useCategories();
  const replaceSplits = useReplaceSplits(transaction.id);
  const removeSplits = useRemoveSplits(transaction.id);

  const income = transaction.direction === 'Income';
  const [lines, setLines] = useState<SplitDraftLine[]>(() => initialLines(transaction));
  const [pickerFor, setPickerFor] = useState<string | null>(null);
  const [keepPickerOpen, setKeepPickerOpen] = useState(false);
  const [notesOpen, setNotesOpen] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);
  const amountRefs = useRef<Record<string, TextInput | null>>({});

  const summary = summarizeSplitDraft(transaction.amount, lines);
  const categoryById = useMemo(() => new Map((categories ?? []).map((c) => [c.id, c])), [categories]);

  // Las categorías del mismo tipo que el movimiento primero; "Sin categoría" siempre disponible.
  const categoryOptions: SelectSheetOption<string>[] = useMemo(() => {
    const all = categories ?? [];
    const sameKind = all.filter((c) => c.isIncome === income);
    const otherKind = all.filter((c) => c.isIncome !== income);
    return [
      ...[...sameKind, ...otherKind].map((c) => ({ value: c.id, label: c.name })),
      { value: NONE, label: 'Sin categoría' },
    ];
  }, [categories, income]);

  const labelFor = (categoryId: string | null) =>
    categoryId === null ? 'Sin categoría' : (categoryById.get(categoryId)?.name ?? 'Categoría');

  const updateLine = (key: string, patch: Partial<SplitDraftLine>) =>
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  const removeLine = (key: string) => setLines((current) => current.filter((line) => line.key !== key));

  /** "+ Agregar categoría": elige categoría y el monto llega prellenado con lo que falta. */
  const addLine = () => {
    const key = nextKey();
    setLines((current) => [
      ...current,
      {
        key,
        categoryId: null,
        amountText: summary.remainingCents > 0 ? centsToInput(summary.remainingCents) : '',
        note: '',
      },
    ]);
    setPickerFor(key);
  };

  /** Terminar sin categorizar todo: lo que falta va a "Sin categoría". */
  const assignRemainderToNone = () => {
    const existing = lines.find((line) => line.categoryId === null);
    if (existing) {
      const cents = (parseCents(existing.amountText) ?? 0) + summary.remainingCents;
      updateLine(existing.key, { amountText: centsToInput(cents) });
      return;
    }
    setLines((current) => [
      ...current,
      { key: nextKey(), categoryId: null, amountText: centsToInput(summary.remainingCents), note: '' },
    ]);
  };

  const trackSaved = (wasSplit: boolean) => {
    const properties = {
      parts: partsBucket(lines.length),
      transactionType: income ? TransactionDirection.Income : TransactionDirection.Expense,
      hasUncategorized: lines.some((line) => line.categoryId === null),
    };
    track(wasSplit ? AnalyticsEvent.TransactionSplitUpdated : AnalyticsEvent.TransactionSplitCreated, properties);
  };

  const save = async () => {
    if (!summary.canSave) {
      return;
    }
    setError(null);
    try {
      await replaceSplits.mutateAsync({ splits: draftToRequest(lines), expectedVersion: transaction.splitVersion });
      trackSaved(transaction.isSplit);
      router.back();
    } catch (caught) {
      setError(errorMessage(caught));
    }
  };

  const confirmRemove = () => {
    Alert.alert(
      'Quitar división',
      'El movimiento vuelve a tener una sola categoría. El movimiento bancario no cambia.',
      [
        { text: 'Cancelar', style: 'cancel' },
        { text: 'Continuar', style: 'destructive', onPress: () => setKeepPickerOpen(true) },
      ],
    );
  };

  const removeWith = async (categoryId: string | null) => {
    setKeepPickerOpen(false);
    setError(null);
    try {
      await removeSplits.mutateAsync({ categoryId, expectedVersion: transaction.splitVersion });
      track(AnalyticsEvent.TransactionSplitRemoved, {
        transactionType: income ? TransactionDirection.Income : TransactionDirection.Expense,
      });
      router.back();
    } catch (caught) {
      setError(errorMessage(caught));
    }
  };

  // Para "¿qué categoría queda?": primero las que ya estaban en la división.
  const keepOptions: SelectSheetOption<string>[] = useMemo(() => {
    const inSplit = transaction.splits
      .map((split) => split.categoryId)
      .filter((value): value is string => value !== null);
    const rest = categoryOptions.filter((option) => option.value !== NONE && !inSplit.includes(option.value));
    return [
      ...inSplit.map((value) => ({ value, label: categoryById.get(value)?.name ?? 'Categoría' })),
      ...rest,
      { value: NONE, label: 'Sin categoría' },
    ];
  }, [transaction.splits, categoryOptions, categoryById]);

  return (
    <SafeAreaView style={styles.root} edges={['top', 'bottom']}>
      <View style={styles.topBar}>
        <Pressable onPress={() => router.back()} hitSlop={12} accessibilityRole="button" accessibilityLabel="Cerrar">
          <Ionicons name="close" size={24} color={colors.text} />
        </Pressable>
        {transaction.isSplit ? (
          <Pressable onPress={confirmRemove} hitSlop={12} accessibilityRole="button">
            <Typo variant="caption" color={colors.danger}>
              Quitar división
            </Typo>
          </Pressable>
        ) : null}
      </View>

      <KeyboardAwareScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        <Typo variant="overline" color={colors.textSecondary}>
          {income ? 'DIVIDIR INGRESO' : 'DIVIDIR GASTO'}
        </Typo>
        <Typo variant="title" tabular>
          {formatCurrency(transaction.signedAmount, { signed: true })}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary} numberOfLines={1}>
          {transaction.merchant ?? transaction.description}
        </Typo>
        <Typo variant="body" color={colors.textSecondary} style={styles.lead}>
          Distribuye este movimiento entre varias categorías.
        </Typo>

        <View style={styles.card}>
          {lines.map((line, index) => {
            const invalid = summary.invalidLines.includes(index) && line.amountText.trim() !== '';
            return (
              <View key={line.key}>
                {index > 0 ? <View style={styles.divider} /> : null}
                <View style={styles.line}>
                  <View style={styles.lineHeader}>
                    <Typo variant="bodyStrong" style={styles.flex} numberOfLines={1}>
                      {labelFor(line.categoryId)}
                    </Typo>
                    <Pressable onPress={() => setPickerFor(line.key)} hitSlop={8} accessibilityRole="button" accessibilityLabel={`Cambiar categoría de ${labelFor(line.categoryId)}`}>
                      <Typo variant="caption" color={colors.text}>
                        Cambiar
                      </Typo>
                    </Pressable>
                    {lines.length > 1 ? (
                      <Pressable onPress={() => removeLine(line.key)} hitSlop={8} accessibilityRole="button" accessibilityLabel={`Quitar ${labelFor(line.categoryId)}`}>
                        <Ionicons name="close-circle-outline" size={20} color={colors.textSecondary} />
                      </Pressable>
                    ) : null}
                  </View>

                  <View style={[styles.amountBox, invalid ? styles.amountInvalid : null]}>
                    <Typo variant="subheading" color={colors.textSecondary}>
                      $
                    </Typo>
                    <TextInput
                      ref={(ref) => {
                        amountRefs.current[line.key] = ref;
                      }}
                      value={line.amountText}
                      onChangeText={(text) => updateLine(line.key, { amountText: text })}
                      placeholder="0.00"
                      placeholderTextColor={colors.textSecondary}
                      keyboardType="decimal-pad"
                      style={styles.amountInput}
                      accessibilityLabel={`Monto para ${labelFor(line.categoryId)}`}
                    />
                  </View>

                  {notesOpen[line.key] || line.note ? (
                    <TextInput
                      value={line.note}
                      onChangeText={(text) => updateLine(line.key, { note: text })}
                      placeholder="Nota (opcional)"
                      placeholderTextColor={colors.textSecondary}
                      maxLength={200}
                      style={styles.noteInput}
                    />
                  ) : (
                    <Pressable onPress={() => setNotesOpen((open) => ({ ...open, [line.key]: true }))} hitSlop={6}>
                      <Typo variant="caption" color={colors.textSecondary}>
                        + Agregar nota
                      </Typo>
                    </Pressable>
                  )}
                </View>
              </View>
            );
          })}
        </View>

        <Button label="+ Agregar categoría" variant="secondary" onPress={addLine} />

        {summary.remainingCents > 0 && summary.invalidLines.length === 0 && lines.length >= 1 ? (
          <Pressable onPress={assignRemainderToNone} style={styles.remainderLink} accessibilityRole="button">
            <Typo variant="caption" color={colors.textSecondary}>
              Dejar {formatCurrency(fromCents(summary.remainingCents))} sin categoría
            </Typo>
          </Pressable>
        ) : null}

        {error ? (
          <Typo variant="caption" color={colors.danger} style={styles.error}>
            {error}
          </Typo>
        ) : null}
      </KeyboardAwareScrollView>

      <View style={styles.footer} accessibilityLiveRegion="polite">
        <SummaryRow label="Total" value={formatCurrency(fromCents(summary.totalCents))} />
        <SummaryRow label="Distribuido" value={formatCurrency(fromCents(summary.distributedCents))} />
        {summary.overCents > 0 ? (
          <SummaryRow label="Te pasaste" value={formatCurrency(fromCents(summary.overCents))} danger />
        ) : (
          <SummaryRow label="Falta" value={formatCurrency(fromCents(summary.remainingCents))} strong={summary.remainingCents > 0} />
        )}
        {summary.blocker && lines.length >= 2 ? (
          <Typo variant="caption" color={summary.overCents > 0 ? colors.danger : colors.textSecondary}>
            {summary.blocker}
          </Typo>
        ) : null}
        <Button
          label="Guardar división"
          onPress={() => void save()}
          disabled={!summary.canSave}
          loading={replaceSplits.isPending}
          loadingLabel="Guardando..."
        />
      </View>

      <SelectSheet
        visible={pickerFor !== null}
        title="Categoría"
        options={categoryOptions}
        selectedValue={lines.find((line) => line.key === pickerFor)?.categoryId ?? NONE}
        onSelect={(value) => {
          const key = pickerFor;
          if (key) {
            updateLine(key, { categoryId: value === NONE ? null : value });
            // Categoría elegida → directo al monto.
            setTimeout(() => amountRefs.current[key]?.focus(), 250);
          }
          setPickerFor(null);
        }}
        onClose={() => setPickerFor(null)}
      />

      <SelectSheet
        visible={keepPickerOpen}
        title="¿Qué categoría queda?"
        options={keepOptions}
        selectedValue=""
        onSelect={(value) => void removeWith(value === NONE ? null : value)}
        onClose={() => setKeepPickerOpen(false)}
      />
    </SafeAreaView>
  );
}

function SummaryRow({ label, value, strong = false, danger = false }: { label: string; value: string; strong?: boolean; danger?: boolean }) {
  return (
    <View style={styles.summaryRow}>
      <Typo variant={strong || danger ? 'bodyStrong' : 'body'} color={danger ? colors.danger : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong || danger ? 'bodyStrong' : 'body'} color={danger ? colors.danger : colors.text} tabular>
        {value}
      </Typo>
    </View>
  );
}

function errorMessage(caught: unknown): string {
  if (caught instanceof ApiError) {
    return Object.values(caught.fieldErrors).find(Boolean) ?? caught.message;
  }
  return 'No pudimos guardar la división. Intenta de nuevo.';
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  padded: {
    padding: spacing.lg,
  },
  topBar: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
  },
  content: {
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.xl,
    gap: spacing.xs,
  },
  lead: {
    marginTop: spacing.sm,
    marginBottom: spacing.lg,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
    marginBottom: spacing.md,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  line: {
    paddingVertical: spacing.md,
    gap: spacing.sm,
  },
  lineHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
  },
  amountBox: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    backgroundColor: colors.background,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.md,
  },
  amountInvalid: {
    borderColor: colors.danger,
  },
  amountInput: {
    flex: 1,
    paddingVertical: spacing.sm,
    color: colors.text,
    fontSize: typography.subheading.fontSize,
    fontWeight: '600',
  },
  noteInput: {
    backgroundColor: colors.background,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    color: colors.text,
    fontSize: typography.caption.fontSize,
  },
  remainderLink: {
    alignSelf: 'center',
    paddingVertical: spacing.md,
  },
  error: {
    marginTop: spacing.md,
  },
  footer: {
    backgroundColor: colors.surface,
    borderTopWidth: 1,
    borderTopColor: colors.border,
    padding: spacing.lg,
    gap: spacing.xs,
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
});
