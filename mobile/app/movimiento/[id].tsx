import { useEffect, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import {
  useCategories,
  useClearInternalTransfer,
  useResolveDuplicate,
  useSetCategory,
  useSetMerchant,
  useSetNote,
  useTransaction,
} from '../../hooks/queries';
import { formatCurrency, formatFullDateTime, maskLabel } from '../../utils/format';

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
  const setNote = useSetNote(id ?? '');
  const setMerchant = useSetMerchant(id ?? '');
  const clearTransfer = useClearInternalTransfer();
  const resolveDuplicate = useResolveDuplicate(id ?? '');

  const [note, setNoteValue] = useState('');
  const [editingCategory, setEditingCategory] = useState(false);
  const [merchantText, setMerchantValue] = useState('');

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

  return (
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

      {data.isInternalTransfer ? (
        <View style={styles.transferNotice}>
          <View style={styles.transferNoticeRow}>
            <Ionicons name="swap-horizontal" size={17} color={colors.textSecondary} />
            <Typo variant="caption" color={colors.textSecondary} style={styles.warningText}>
              Confirmaste que esto es una transferencia entre tus cuentas: mueve el saldo de la cuenta, pero no
              cuenta como gasto ni como ingreso.
            </Typo>
          </View>
          <Button
            label="Deshacer transferencia"
            compact
            variant="ghost"
            loading={clearTransfer.isPending}
            onPress={() => clearTransfer.mutate(data.id)}
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
          </Card>
        ) : (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.categoryList}>
            {(categories ?? []).map((category) => (
              <Pressable
                key={category.id}
                onPress={() => {
                  setCategory.mutate(category.id, { onSuccess: () => setEditingCategory(false) });
                }}
                style={[
                  styles.categoryChip,
                  data.categoryId === category.id ? styles.categoryChipSelected : null,
                ]}
              >
                <Typo variant="caption" color={data.categoryId === category.id ? colors.onPrimary : colors.text}>
                  {category.name}
                </Typo>
              </Pressable>
            ))}
          </ScrollView>
        )}
      </View>

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
  categoryList: {
    gap: spacing.sm,
    paddingRight: spacing.lg,
  },
  categoryChip: {
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm + 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
  },
  categoryChipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
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
