import { useEffect, useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { WithdrawalCard } from '../../components/transactions/WithdrawalCard';
import { colors, radius, spacing, typography } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SectionHeader, SelectSheet, SkeletonCard, Typo } from '../../components/ui';
import {
  useCategories,
  useClearInternalTransfer,
  useCreateCategory,
  useReclassifyCardMovement,
  useResolveDuplicate,
  useRulePreview,
  useSetCategory,
  useSetMerchant,
  useSetNote,
  useTransaction,
  useUpdateCategory,
  useWithdrawalCandidates,
} from '../../hooks/queries';
import { CategoryChipList } from '../../components/categories/CategoryChipList';
import { CategoryFormSheet } from '../../components/categories/CategoryFormSheet';
import { ApiError } from '../../services/apiClient';
import { formatCurrency, formatFullDateTime, maskLabel } from '../../utils/format';
import { MOVEMENT_TYPE_HINTS, MOVEMENT_TYPE_LABELS } from '../../utils/creditCards';
import type { CardMovementType, Category, RulePreview } from '../../types/api';

const CARD_TYPES: CardMovementType[] = ['Purchase', 'Refund', 'Payment', 'Interest', 'Fee', 'CashAdvance', 'Adjustment'];
const NEUTRAL_CARD_TYPES: ReadonlySet<CardMovementType> = new Set(['Payment', 'CashAdvance', 'Adjustment']);

/**
 * "Categorización personal" (punto 11): por qué el movimiento tiene la
 * categoría que tiene. "Manual" ya se ve arriba como el badge "Ajustada por
 * ti", así que aquí no repite nada; "Imported"/"Uncategorized" (el resultado
 * sin interés de caer en la categoría genérica) tampoco aportan nada nuevo.
 */
const categorySourceLabel: Record<string, string | null> = {
  Manual: null,
  ManualSplit: null,
  UserRule: 'Automática, según una regla creada por ti',
  SystemRule: 'Automática, según las reglas de Fino',
  Imported: null,
  Uncategorized: null,
};

const sourceLabel: Record<string, string> = {
  Import: 'Importado desde estado de cuenta',
  Email: 'Detectado desde notificación bancaria',
  Api: 'Sincronizado automáticamente',
  Webhook: 'Recibido en tiempo real',
  Manual: 'Agregado manualmente',
};

const statusLabel: Record<string, string> = {
  Posted: 'Confirmado',
  Pending: 'Por confirmar',
  NeedsReview: 'Posible duplicado',
  Ignored: 'Ignorado',
};

export default function TransactionDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { data, isLoading, isError } = useTransaction(id ?? '');
  const { data: categories } = useCategories();

  const setCategory = useSetCategory(id ?? '');
  const rulePreview = useRulePreview();
  const setNote = useSetNote(id ?? '');
  const setMerchant = useSetMerchant(id ?? '');
  const clearTransfer = useClearInternalTransfer();
  const resolveDuplicate = useResolveDuplicate(id ?? '');
  const reclassify = useReclassifyCardMovement();
  const [typePickerVisible, setTypePickerVisible] = useState(false);

  // §10: el retiro se busca en la lista de candidatos que la bandeja ya consulta, en
  // vez de pedirle al servidor que evalúe este movimiento suelto. Una petición menos,
  // y una sola fuente de verdad sobre qué es candidato y qué no.
  const { data: withdrawalCandidates } = useWithdrawalCandidates();
  const withdrawalCandidate = useMemo(
    () => (withdrawalCandidates ?? []).find((candidate) => candidate.transactionId === id) ?? null,
    [withdrawalCandidates, id],
  );

  const [note, setNoteValue] = useState('');
  const [editingCategory, setEditingCategory] = useState(false);
  const [merchantText, setMerchantValue] = useState('');

  // Categorías personalizadas: crear/editar una categoría propia sin salir
  // de "Cambiar categoría". `categoryFormTarget` null => crear, category => editar.
  const [categoryFormVisible, setCategoryFormVisible] = useState(false);
  const [categoryFormTarget, setCategoryFormTarget] = useState<Category | null>(null);
  const [categoryFormError, setCategoryFormError] = useState<string | null>(null);
  const createCategory = useCreateCategory();
  const updateCategory = useUpdateCategory();

  useEffect(() => {
    setNoteValue(data?.note ?? '');
  }, [data?.note]);

  useEffect(() => {
    // `merchant` ya es el "efectivo": la corrección si existe, si no la
    // suposición automática. Editarlo y guardar en blanco vuelve a esa
    // suposición (Entregable 12) -- nunca toca la descripción del banco.
    setMerchantValue(data?.merchant ?? '');
  }, [data?.merchant]);

  if (isError) {
    // Entregable 22: reaching here from a stale push notification (its
    // deep-link payload still names a transaction that "eliminar mis
    // movimientos" or "eliminar una cuenta" already erased) used to fall
    // through to the loading branch below forever -- isLoading is false and
    // data stays undefined, so nothing ever left the skeleton state.
    return (
      <Screen>
        <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
          <Ionicons name="chevron-back" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Volver
          </Typo>
        </Pressable>
        <EmptyState
          icon="alert-circle-outline"
          title="No encontramos este movimiento"
          body="Puede que ya no exista -- por ejemplo, si eliminaste tus movimientos o esta cuenta."
        />
      </Screen>
    );
  }

  if (isLoading || !data) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  const income = data.direction === 'Income';

  /**
   * "Categorización personal" (puntos 5/6/7): antes de aplicar la categoría
   * elegida, se pregunta al backend qué patrón usaría y cuántos movimientos
   * ya guardados coincidirían -- nunca se recategoriza nada sin que la
   * persona vea el conteo y confirme explícitamente (punto 7, "nunca
   * ejecutar una actualización masiva sin confirmación explícita").
   */
  const chooseCategory = (category: Category) => {
    if (category.id === data.categoryId) {
      setEditingCategory(false);
      return;
    }

    rulePreview.mutate(
      { transactionId: data.id, categoryId: category.id },
      {
        onSuccess: (preview) => presentScopeChoice(category, preview),
        onError: (error) => {
          Alert.alert(
            'No pudimos evaluar la categoría',
            error instanceof ApiError ? error.message : 'Inténtalo de nuevo en un momento.',
          );
        },
      },
    );
  };

  const applyCategory = (categoryId: string, createRule: boolean, applyToExistingMatches: boolean) => {
    setCategory.mutate(
      { categoryId, createRule, applyToExistingMatches },
      {
        onSuccess: (detail) => {
          setEditingCategory(false);
          if (applyToExistingMatches && detail.recategorizedCount) {
            Alert.alert(
              'Movimientos actualizados',
              `Se actualizaron ${detail.recategorizedCount} movimiento${detail.recategorizedCount === 1 ? '' : 's'} anterior${detail.recategorizedCount === 1 ? '' : 'es'} a "${detail.categoryName ?? ''}".`,
            );
          }
        },
        onError: (error) => {
          Alert.alert(
            'No pudimos guardar la categoría',
            error instanceof ApiError ? error.message : 'Inténtalo de nuevo en un momento.',
          );
        },
      },
    );
  };

  const presentScopeChoice = (category: Category, preview: RulePreview) => {
    // Punto 18: un patrón demasiado genérico nunca se ofrece como regla --
    // solo se cambia este movimiento, sin preguntar nada más.
    if (preview.isTooGeneric) {
      applyCategory(category.id, false, false);
      return;
    }

    const conflictNote = preview.conflictingRuleId
      ? ` Ya tienes una regla que categoriza "${preview.pattern}" como ${preview.conflictingCategoryName}; se actualizará para usar ${category.name}.`
      : '';

    const message =
      (preview.matchedCount > 0
        ? `Encontramos ${preview.matchedCount} movimiento${preview.matchedCount === 1 ? '' : 's'} similar${preview.matchedCount === 1 ? '' : 'es'}.`
        : '¿Cómo quieres aplicar este cambio?') + conflictNote;

    const buttons: { text: string; style?: 'cancel' | 'destructive'; onPress?: () => void }[] = [
      { text: 'Solo este movimiento', onPress: () => applyCategory(category.id, false, false) },
      { text: 'Este y futuros movimientos', onPress: () => applyCategory(category.id, true, false) },
    ];

    if (preview.matchedCount > 0) {
      buttons.push({
        text: `Este, anteriores y futuros (${preview.matchedCount})`,
        onPress: () => applyCategory(category.id, true, true),
      });
    }

    buttons.push({ text: 'Cancelar', style: 'cancel' });

    Alert.alert(`Aplicar automáticamente: "${preview.pattern}" → ${category.name}`, message, buttons);
  };

  // Categorías personalizadas: "+ Nueva" abre la hoja en modo crear; el
  // lápiz de una categoría propia la abre en modo editar. Una vez creada,
  // se elige de inmediato para este movimiento -- reutiliza chooseCategory,
  // así que sigue pasando por el mismo preview/confirmación de siempre.
  const openCreateCategory = () => {
    setCategoryFormError(null);
    setCategoryFormTarget(null);
    setCategoryFormVisible(true);
  };

  const openEditCategory = (category: Category) => {
    setCategoryFormError(null);
    setCategoryFormTarget(category);
    setCategoryFormVisible(true);
  };

  const submitCategoryForm = (input: { name: string; icon: string; color: string }) => {
    setCategoryFormError(null);

    if (categoryFormTarget) {
      updateCategory.mutate(
        { id: categoryFormTarget.id, ...input },
        {
          onSuccess: () => setCategoryFormVisible(false),
          onError: (error) => {
            setCategoryFormError(error instanceof ApiError ? error.message : 'No se pudo guardar la categoría.');
          },
        },
      );
      return;
    }

    createCategory.mutate(input, {
      onSuccess: (created) => {
        setCategoryFormVisible(false);
        chooseCategory(created);
      },
      onError: (error) => {
        setCategoryFormError(error instanceof ApiError ? error.message : 'No se pudo crear la categoría.');
      },
    });
  };

  const categorySourceHint = categorySourceLabel[data.categorySource];

  return (
    <>
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.amountBlock}>
        <Typo variant="display" tabular color={income ? colors.success : colors.text}>
          {formatCurrency(data.signedAmount, { signed: true })}
        </Typo>
        <View style={styles.merchantHeadingRow}>
          <Typo variant="heading">{data.merchant ?? data.description}</Typo>
          {data.merchantCorrected ? <Badge label="Corregido por ti" tone="accent" /> : null}
        </View>
        <Typo variant="caption" color={colors.textSecondary}>
          {formatFullDateTime(data.transactionDate)}
        </Typo>
      </View>

      {data.status === 'NeedsReview' ? (
        <View style={styles.warning}>
          <View style={styles.transferNoticeRow}>
            <Ionicons name="alert-circle-outline" size={17} color={colors.warning} />
            <Typo variant="caption" color={colors.textSecondary} style={styles.warningText}>
              Este movimiento se parece a otro que ya tenías. Lo guardamos aparte para que decidas tú:
              no lo eliminamos automáticamente.
            </Typo>
          </View>
          <View style={styles.duplicateActions}>
            <Button
              label="Es un movimiento distinto"
              compact
              variant="ghost"
              loading={resolveDuplicate.isPending}
              onPress={() => resolveDuplicate.mutate(true)}
            />
            <Button
              label="Ya lo tenía, ignorar"
              compact
              variant="ghost"
              loading={resolveDuplicate.isPending}
              onPress={() => resolveDuplicate.mutate(false)}
            />
          </View>
        </View>
      ) : null}

      {/* §10: "¿Qué pasó con estos $100?" -- solo cuando el detector cree que es un
          retiro y todavía nadie ha decidido. La tarjeta es la MISMA que la bandeja
          "Por revisar", para que la pregunta se vea igual la encuentre donde la
          encuentre. */}
      {withdrawalCandidate ? (
        <View style={styles.withdrawalBlock}>
          <WithdrawalCard candidate={withdrawalCandidate} />
        </View>
      ) : null}

      {data.isInternalTransfer ? (
        <View style={styles.transferNotice}>
          <View style={styles.transferNoticeRow}>
            <Ionicons name="swap-horizontal" size={17} color={colors.textSecondary} />
            <Typo variant="caption" color={colors.textSecondary} style={styles.warningText}>
              {data.cardMovementType === 'Payment'
                ? 'Pago a tu tarjeta: baja la deuda y no cuenta como gasto ni como ingreso -- el gasto fueron las compras.'
                : data.cardMovementType && NEUTRAL_CARD_TYPES.has(data.cardMovementType)
                  ? `${MOVEMENT_TYPE_LABELS[data.cardMovementType]}: cambia la deuda de la tarjeta, pero no es gasto ni ingreso.`
                  : 'Confirmaste que esto es una transferencia entre tus cuentas: mueve el saldo de la cuenta, pero no cuenta como gasto ni como ingreso.'}
            </Typo>
          </View>
          {data.internalTransferLinkId ? (
            <Button
              label={data.cardMovementType === 'Payment' ? 'Desvincular del débito del banco' : 'Deshacer transferencia'}
              compact
              variant="ghost"
              loading={clearTransfer.isPending}
              onPress={() => clearTransfer.mutate(data.id)}
            />
          ) : null}
        </View>
      ) : null}

      {data.accountIsCreditCard ? (
        <View style={styles.section}>
          <SectionHeader title="Tarjeta" actionLabel="Cambiar tipo" onAction={() => setTypePickerVisible(true)} />
          <Card>
            <Typo variant="bodyStrong">{data.cardMovementType ? MOVEMENT_TYPE_LABELS[data.cardMovementType] : 'Sin tipo'}</Typo>
            {data.cardMovementType ? (
              <Typo variant="caption" color={colors.textSecondary}>
                {MOVEMENT_TYPE_HINTS[data.cardMovementType]}
              </Typo>
            ) : null}
            {data.cardMovementType === 'Purchase' && data.installmentPlanId === null && data.status !== 'Ignored' ? (
              <Button
                label="Diferir en cuotas"
                variant="secondary"
                compact
                onPress={() =>
                  router.push({ pathname: '/tarjetas/diferir', params: { id: data.financialAccountId, transactionId: data.id } })
                }
              />
            ) : null}
            {data.installmentPlanId ? (
              <Button
                label="Ver sus cuotas"
                variant="secondary"
                compact
                onPress={() =>
                  router.push({ pathname: '/tarjetas/[id]', params: { id: data.financialAccountId, tab: 'installments' } })
                }
              />
            ) : null}
          </Card>
          <SelectSheet
            visible={typePickerVisible}
            title="¿Qué es este movimiento?"
            options={CARD_TYPES.map((value) => ({ value, label: MOVEMENT_TYPE_LABELS[value] }))}
            selectedValue={data.cardMovementType ?? 'Purchase'}
            onSelect={(value) => {
              setTypePickerVisible(false);
              if (value === data.cardMovementType) {
                return;
              }
              reclassify.mutate(
                { id: data.financialAccountId, transactionId: data.id, type: value },
                { onError: (error) => Alert.alert('No pudimos cambiar el tipo', error.message) },
              );
            }}
            onClose={() => setTypePickerVisible(false)}
          />
        </View>
      ) : null}

      <Card>
        <Field label="Cuenta" value={`${data.accountAlias}  ${maskLabel(data.accountMask)}`} />
        <Divider />
        <Field label="Descripción" value={data.description} />
        <Divider />
        <Field label="Referencia" value={data.externalReference ?? 'Sin referencia'} />
        <Divider />
        <Field label="Estado" value={statusLabel[data.status] ?? data.status} />
        <Divider />
        <Field label="Origen" value={sourceLabel[data.source] ?? data.source} />
      </Card>

      {data.isSplit ? (
        // Movimientos divididos: las partes reemplazan a la categoría única, y
        // "Editar división" es el único camino para cambiarlas -- nunca una
        // categoría global que borre la división sin querer.
        <View style={styles.section}>
          <SectionHeader
            title="Categorías"
            actionLabel="Editar división"
            onAction={() => router.push(`/movimiento/dividir/${data.id}`)}
          />
          <Card>
            {data.splits.map((split, index) => (
              <View key={split.id}>
                {index > 0 ? <Divider /> : null}
                <View style={styles.splitRow}>
                  <View style={styles.splitText}>
                    <Typo variant="body">{split.categoryName}</Typo>
                    {split.note ? (
                      <Typo variant="caption" color={colors.textSecondary} numberOfLines={1}>
                        {split.note}
                      </Typo>
                    ) : null}
                  </View>
                  <Typo variant="bodyStrong" tabular>
                    {formatCurrency(split.amount)}
                  </Typo>
                </View>
              </View>
            ))}
            <Typo variant="caption" color={colors.textSecondary} style={styles.categorySourceHint}>
              {data.splits.length} categorías · {formatCurrency(data.amount)} distribuido
            </Typo>
          </Card>
        </View>
      ) : (
        <View style={styles.section}>
          <SectionHeader
            title="Categoría"
            actionLabel={editingCategory ? 'Cancelar' : 'Cambiar'}
            onAction={() => setEditingCategory(!editingCategory)}
          />

          {!editingCategory ? (
            <Card>
              <View style={styles.categoryRow}>
                <Typo variant="body">{data.categoryName ?? 'Sin categoría'}</Typo>
                {data.categoryManuallySet ? <Badge label="Ajustada por ti" tone="accent" /> : null}
              </View>
              {/* "Categorización personal" (punto 11): explica de dónde salió la
                 categoría cuando eso aporta algo -- nunca para una corrección
                 manual (ya lo dice el badge de arriba) ni para el resultado sin
                 interés de un fallback sin regla. */}
              {categorySourceHint ? (
                <Typo variant="caption" color={colors.textSecondary} style={styles.categorySourceHint}>
                  {categorySourceHint}
                </Typo>
              ) : null}
            </Card>
          ) : (
            <CategoryChipList
              categories={categories ?? []}
              selectedId={data.categoryId}
              onSelect={chooseCategory}
              onCreateNew={openCreateCategory}
              onEdit={openEditCategory}
              disabled={rulePreview.isPending || setCategory.isPending}
              loading={rulePreview.isPending}
            />
          )}

          {!editingCategory && !data.isInternalTransfer ? (
            <Pressable
              onPress={() => router.push(`/movimiento/dividir/${data.id}`)}
              accessibilityRole="button"
              accessibilityHint="Reparte este movimiento entre varias categorías"
              style={styles.splitAction}
              hitSlop={6}
            >
              <Ionicons name="pie-chart-outline" size={15} color={colors.textSecondary} />
              <Typo variant="caption" color={colors.textSecondary}>
                Dividir movimiento
              </Typo>
            </Pressable>
          ) : null}
        </View>
      )}

      <View style={styles.section}>
        <SectionHeader title="Comercio" />
        <TextInput
          value={merchantText}
          onChangeText={setMerchantValue}
          placeholder="Nombre del comercio"
          placeholderTextColor={colors.textSecondary}
          style={styles.merchantInput}
        />
        <Typo variant="caption" color={colors.textSecondary} style={styles.merchantHint}>
          Fino lo detecta automáticamente desde la descripción del banco. Corrígelo si no es exacto, o
          déjalo vacío para volver a la sugerencia automática.
        </Typo>

        {merchantText.trim() !== (data.merchant ?? '') || data.merchantCorrected ? (
          <View style={styles.merchantActions}>
            {merchantText.trim() !== (data.merchant ?? '') ? (
              <Button
                label="Guardar comercio"
                compact
                loading={setMerchant.isPending}
                onPress={() =>
                  setMerchant.mutate(merchantText.trim().length === 0 ? null : merchantText.trim())
                }
              />
            ) : null}

            {data.merchantCorrected ? (
              <Button
                label="Quitar corrección"
                compact
                variant="ghost"
                loading={setMerchant.isPending}
                onPress={() => setMerchant.mutate(null)}
              />
            ) : null}
          </View>
        ) : null}
      </View>

      <View style={styles.section}>
        <SectionHeader title="Nota" />
        <TextInput
          value={note}
          onChangeText={setNoteValue}
          placeholder="Agrega una nota para recordar de qué se trató"
          placeholderTextColor={colors.textSecondary}
          multiline
          style={styles.noteInput}
        />

        {note !== (data.note ?? '') ? (
          <View style={styles.noteAction}>
            <Button
              label="Guardar nota"
              compact
              loading={setNote.isPending}
              onPress={() => setNote.mutate(note.trim().length === 0 ? null : note.trim())}
            />
          </View>
        ) : null}
      </View>
    </Screen>

    <CategoryFormSheet
      visible={categoryFormVisible}
      category={categoryFormTarget}
      onClose={() => setCategoryFormVisible(false)}
      onSubmit={submitCategoryForm}
      submitting={createCategory.isPending || updateCategory.isPending}
      errorMessage={categoryFormError}
    />
    </>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.field}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="body" style={styles.fieldValue}>
        {value}
      </Typo>
    </View>
  );
}

function Divider() {
  return <View style={styles.divider} />;
}

const styles = StyleSheet.create({
  splitRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    paddingVertical: spacing.sm,
  },
  splitText: {
    flex: 1,
  },
  splitAction: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: spacing.xs,
    marginTop: spacing.md,
  },
  withdrawalBlock: {
    marginBottom: spacing.lg,
  },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  amountBlock: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  warning: {
    gap: spacing.sm,
    backgroundColor: 'rgba(228, 168, 83, 0.14)',
    borderRadius: radius.md,
    padding: spacing.md,
    marginBottom: spacing.lg,
    alignItems: 'flex-start',
  },
  warningText: {
    flex: 1,
  },
  duplicateActions: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  transferNotice: {
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
    marginBottom: spacing.lg,
    gap: spacing.sm,
    alignItems: 'flex-start',
  },
  transferNoticeRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.lg,
    paddingVertical: spacing.sm,
  },
  fieldValue: {
    flex: 1,
    textAlign: 'right',
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  section: {
    marginTop: spacing.xxl,
  },
  categoryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  categorySourceHint: {
    marginTop: spacing.xs,
  },
  noteInput: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    minHeight: 96,
    textAlignVertical: 'top',
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
  },
  noteAction: {
    marginTop: spacing.md,
    alignItems: 'flex-start',
  },
  merchantHeadingRow: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  merchantInput: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.lg,
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
  },
  merchantHint: {
    marginTop: spacing.sm,
  },
  merchantActions: {
    marginTop: spacing.md,
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    alignItems: 'flex-start',
  },
});
