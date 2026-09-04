import { useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import * as DocumentPicker from 'expo-document-picker';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, Screen, SectionHeader, Typo } from '../../components/ui';
import { api } from '../../services/endpoints';
import { ApiError } from '../../services/apiClient';
import { useRefreshAfterImport } from '../../hooks/queries';
import { formatCurrency, formatDayHeading } from '../../utils/format';
import type { ImportPreview, ImportResult } from '../../types/api';

type Step = 'pick' | 'working' | 'preview' | 'done';

const ACCEPTED_TYPES = [
  'text/csv',
  'text/comma-separated-values',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'text/plain',
];

/**
 * Archivo → Validación → Preview → Importación → Resultado.
 * Nothing is written until the user confirms what they are looking at.
 */
export default function ImportScreen() {
  const router = useRouter();
  const { accountId } = useLocalSearchParams<{ accountId: string }>();
  const refreshAfterImport = useRefreshAfterImport();

  const [step, setStep] = useState<Step>('pick');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [excluded, setExcluded] = useState<Set<string>>(new Set());

  const pickAndUpload = async () => {
    setError(null);

    const picked = await DocumentPicker.getDocumentAsync({
      type: ACCEPTED_TYPES,
      copyToCacheDirectory: true,
      multiple: false,
    });

    if (picked.canceled || !picked.assets?.[0] || !accountId) {
      return;
    }

    const asset = picked.assets[0];
    setStep('working');

    try {
      const response = await api.imports.upload(accountId, {
        uri: asset.uri,
        name: asset.name,
        mimeType: asset.mimeType ?? 'text/csv',
      });

      setPreview(response);
      setStep('preview');

      if (response.status === 'Failed') {
        setError(response.failureReason ?? 'No pudimos leer el archivo.');
      }
    } catch (uploadError) {
      setError(uploadError instanceof ApiError ? uploadError.message : 'No pudimos subir el archivo.');
      setStep('pick');
    }
  };

  const confirm = async () => {
    if (!preview) {
      return;
    }

    setStep('working');
    try {
      const confirmation = await api.imports.confirm(preview.importId, Array.from(excluded), false);
      setResult(confirmation);
      refreshAfterImport();
      setStep('done');
    } catch (confirmError) {
      setError(confirmError instanceof ApiError ? confirmError.message : 'No pudimos importar los movimientos.');
      setStep('preview');
    }
  };

  const toggleRow = (rowId: string) => {
    setExcluded((current) => {
      const next = new Set(current);
      if (next.has(rowId)) {
        next.delete(rowId);
      } else {
        next.add(rowId);
      }
      return next;
    });
  };

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      {step === 'pick' ? (
        <View style={styles.block}>
          <Typo variant="title">Importar movimientos</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Sube el estado de cuenta que descargaste de tu banco. Aceptamos CSV y XLSX.
            Te mostramos todo antes de guardar nada.
          </Typo>

          {error ? (
            <View style={styles.errorBox}>
              <Ionicons name="alert-circle-outline" size={17} color={colors.danger} />
              <Typo variant="caption" color={colors.danger} style={styles.flex}>
                {error}
              </Typo>
            </View>
          ) : null}

          <View style={styles.steps}>
            <StepHint index={1} label="Elige el archivo" />
            <StepHint index={2} label="Revisamos y detectamos duplicados" />
            <StepHint index={3} label="Confirmas e importamos" />
          </View>

          <Button label="Elegir archivo" onPress={() => void pickAndUpload()} />
        </View>
      ) : null}

      {step === 'working' ? (
        <View style={styles.working}>
          <ActivityIndicator color={colors.textSecondary} />
          <Typo variant="body" color={colors.textSecondary}>
            Procesando tu archivo…
          </Typo>
        </View>
      ) : null}

      {step === 'preview' && preview ? (
        <View style={styles.block}>
          <Typo variant="title">
            {preview.status === 'Failed' ? 'No pudimos leerlo' : `Encontramos ${preview.totalRows} movimientos`}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {preview.fileName}
            {preview.parserCode ? `  ·  ${preview.parserCode}` : ''}
          </Typo>

          {preview.status === 'Failed' ? (
            <>
              <View style={styles.errorBox}>
                <Ionicons name="alert-circle-outline" size={17} color={colors.danger} />
                <Typo variant="caption" color={colors.danger} style={styles.flex}>
                  {preview.failureReason ?? error}
                </Typo>
              </View>
              <Button label="Probar con otro archivo" onPress={() => setStep('pick')} />
            </>
          ) : (
            <>
              <View style={styles.summaryRow}>
                <Summary label="Ingresos" value={formatCurrency(preview.incomeTotal)} tone={colors.success} />
                <Summary label="Gastos" value={formatCurrency(preview.expenseTotal)} tone={colors.text} />
                <Summary label="Duplicados" value={String(preview.duplicateRows)} tone={colors.textSecondary} />
              </View>

              {preview.previouslyImportedFile ? (
                <View style={styles.noticeBox}>
                  <Ionicons name="copy-outline" size={17} color={colors.warning} />
                  <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
                    Ya importaste este mismo archivo antes. Puedes seguir: los movimientos
                    repetidos aparecen abajo como duplicados y no se guardan otra vez.
                  </Typo>
                </View>
              ) : null}

              {preview.probableDuplicateRows > 0 ? (
                <View style={styles.noticeBox}>
                  <Ionicons name="information-circle-outline" size={17} color={colors.warning} />
                  <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
                    {preview.probableDuplicateRows} movimiento(s) se parecen a otros que ya tienes.
                    Los importamos marcados para que los revises: nunca borramos información por nuestra cuenta.
                  </Typo>
                </View>
              ) : null}

              {preview.invalidRows > 0 ? (
                <View style={styles.noticeBox}>
                  <Ionicons name="warning-outline" size={17} color={colors.warning} />
                  <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
                    {preview.invalidRows} fila(s) no se pudieron leer y se omitirán.
                  </Typo>
                </View>
              ) : null}

              <SectionHeader title="Vista previa" />
              <ScrollView style={styles.rows} nestedScrollEnabled>
                {preview.rows.slice(0, 60).map((row) => {
                  const isExcluded = excluded.has(row.id);
                  const isDuplicate = row.status === 'ExactDuplicate';
                  const invalid = row.status === 'Invalid';

                  return (
                    <Pressable
                      key={row.id}
                      onPress={() => (isDuplicate || invalid ? undefined : toggleRow(row.id))}
                      style={[styles.row, isExcluded || isDuplicate || invalid ? styles.rowMuted : null]}
                    >
                      <View style={styles.flex}>
                        <Typo variant="body" numberOfLines={1}>
                          {row.description ?? `Fila ${row.rowNumber}`}
                        </Typo>
                        <Typo variant="caption" color={colors.textSecondary}>
                          {row.transactionDate ? formatDayHeading(row.transactionDate) : (row.error ?? 'Sin fecha')}
                        </Typo>
                      </View>

                      {isDuplicate ? (
                        <Badge label="Ya lo tienes" tone="neutral" />
                      ) : row.status === 'ProbableDuplicate' ? (
                        <Badge label="Revisar" tone="attention" />
                      ) : invalid ? (
                        <Badge label="Error" tone="danger" />
                      ) : (
                        <Typo
                          variant="bodyStrong"
                          tabular
                          color={row.direction === 'Income' ? colors.success : colors.text}
                        >
                          {row.amount === null
                            ? '—'
                            : formatCurrency(row.direction === 'Income' ? row.amount : -row.amount, { signed: true })}
                        </Typo>
                      )}
                    </Pressable>
                  );
                })}
              </ScrollView>

              <Button
                label={`Importar ${Math.max(0, preview.newRows + preview.probableDuplicateRows - excluded.size)} movimientos`}
                onPress={() => void confirm()}
                disabled={preview.newRows + preview.probableDuplicateRows === 0}
              />
              <Button label="Cancelar" variant="ghost" onPress={() => router.back()} />
            </>
          )}
        </View>
      ) : null}

      {step === 'done' && result ? (
        <View style={styles.block}>
          <View style={styles.doneIcon}>
            <Ionicons name="checkmark" size={26} color={colors.onAccent} />
          </View>

          <Typo variant="title">Listo</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Importamos {result.importedCount} movimiento(s).
            {result.skippedDuplicates > 0 ? ` Omitimos ${result.skippedDuplicates} duplicado(s).` : ''}
            {result.flaggedForReview > 0 ? ` ${result.flaggedForReview} quedaron marcados para revisar.` : ''}
          </Typo>

          <Card tone="secondary">
            <Typo variant="caption" color={colors.textSecondary}>
              Nuevo saldo {result.balanceType === 'Verified' ? 'verificado' : 'estimado'}
            </Typo>
            <Typo variant="title" tabular>
              {formatCurrency(result.newEstimatedBalance)}
            </Typo>
          </Card>

          <Button label="Ver mis movimientos" onPress={() => router.replace('/(tabs)/movimientos')} />
        </View>
      ) : null}
    </Screen>
  );
}

function Summary({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <View style={styles.summaryTile}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="subheading" color={tone} tabular>
        {value}
      </Typo>
    </View>
  );
}

function StepHint({ index, label }: { index: number; label: string }) {
  return (
    <View style={styles.stepHint}>
      <View style={styles.stepBullet}>
        <Typo variant="overline" color={colors.textSecondary}>
          {index}
        </Typo>
      </View>
      <Typo variant="body" color={colors.textSecondary}>
        {label}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  block: {
    gap: spacing.lg,
  },
  steps: {
    gap: spacing.md,
    marginVertical: spacing.sm,
  },
  stepHint: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  stepBullet: {
    width: 26,
    height: 26,
    borderRadius: 13,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  working: {
    alignItems: 'center',
    gap: spacing.lg,
    paddingVertical: spacing.xxxl,
  },
  summaryRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  summaryTile: {
    flex: 1,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.xs,
  },
  errorBox: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: 'rgba(216, 102, 91, 0.12)',
    borderRadius: radius.md,
    padding: spacing.md,
  },
  noticeBox: {
    flexDirection: 'row',
    gap: spacing.sm,
    backgroundColor: 'rgba(228, 168, 83, 0.14)',
    borderRadius: radius.md,
    padding: spacing.md,
  },
  rows: {
    maxHeight: 320,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
  },
  rowMuted: {
    opacity: 0.45,
  },
  doneIcon: {
    width: 56,
    height: 56,
    borderRadius: 20,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
