import { useCallback, useState } from 'react';
import { Image, Linking, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, Screen, Typo } from '../../components/ui';
import { useToast } from '../../components/ui/Toast';
import { useCreateQuickTransaction } from '../../hooks/queries';
import { useReceiptScan } from '../../hooks/useReceiptScan';
import { createRequestId } from '../../utils/quickEntry/requestId';
import { formatCurrency, formatShortDate } from '../../utils/format';
import { ReceiptSource, ReceiptWarning } from '../../lib/receipts/types';
import { MatchConfidence } from '../../lib/receipts/matchTransaction';
import { AnalyticsEvent, AnalyticsSource, ErrorReason, track } from '../../services/analytics';

/**
 * §2-§19: el flujo completo de "Escanear factura".
 *
 * Una sola ruta para las dos vías (cámara y galería) porque el §2 lo pide
 * explícitamente: "No crear dos implementaciones distintas."
 *
 * Esta pantalla NO sabe qué motor de OCR hay debajo, ni cómo se decide el
 * total, ni cómo se buscan los duplicados: todo eso vive en useReceiptScan y
 * en lib/receipts. Aquí solo se pinta estado y se recogen correcciones.
 *
 * Y sobre todo: NO crea movimientos por su cuenta. Al guardar usa
 * `useCreateQuickTransaction`, el mismo hook que el teclado, el texto natural
 * y la voz (§36). No hay un sistema de gastos para facturas.
 */
export default function ReceiptScanScreen() {
  const router = useRouter();
  const toast = useToast();
  const create = useCreateQuickTransaction();
  const { state, fromCamera, fromGallery, retry, reset } = useReceiptScan();
  const [saving, setSaving] = useState(false);

  const close = useCallback(() => {
    reset();
    router.back();
  }, [reset, router]);

  const save = useCallback(async () => {
    if (!state.draft || saving) {
      return;
    }

    setSaving(true);

    try {
      await create.mutateAsync({
        amount: state.draft.amount,
        direction: 'Expense',
        description: state.draft.description,
        // Sin categoría explícita decide el motor de reglas del servidor, que
        // es el que conoce lo que esta persona ya enseñó (§20, prioridades 1-2).
        categoryId: state.draft.categoryId ?? undefined,
        occurredAt: state.draft.occurredAt,
        financialAccountId: state.draft.financialAccountId,
        clientRequestId: createRequestId(),
      });

      track(AnalyticsEvent.ReceiptSaved, {
        source: state.source ?? AnalyticsSource.Unknown,
        matchedExisting: false,
      });

      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => undefined);

      const amount = state.draft.amount;
      close();
      toast.show({ message: `${formatCurrency(amount)} registrado` });
    } catch {
      toast.show({ message: 'No pudimos guardar el movimiento.', tone: 'error' });
    } finally {
      setSaving(false);
    }
  }, [close, create, saving, state.draft, state.source, toast]);

  return (
    <Screen>
      <Pressable onPress={close} hitSlop={12} style={styles.back} accessibilityRole="button" accessibilityLabel="Cerrar">
        <Ionicons name="close" size={22} color={colors.text} />
      </Pressable>

      {state.stage === 'idle' ? <SourcePicker onCamera={fromCamera} onGallery={fromGallery} /> : null}

      {state.stage === 'analyzing' ? <Analyzing imageUri={state.imageUri} /> : null}

      {state.stage === 'failed' ? (
        <Failure
          state={state}
          onRetry={() => void retry(state.source ?? ReceiptSource.Camera)}
          onGallery={() => void fromGallery()}
          onManual={() => {
            close();
            router.push('/movimiento/nuevo');
          }}
        />
      ) : null}

      {state.stage === 'review' && state.draft ? (
        <Review state={state} saving={saving} onSave={() => void save()} onRetry={() => void retry(state.source ?? ReceiptSource.Camera)} />
      ) : null}
    </Screen>
  );
}

/** §3: la pantalla inicial. Dos caminos y una promesa honesta. */
function SourcePicker({ onCamera, onGallery }: { onCamera: () => void; onGallery: () => void }) {
  return (
    <View style={styles.stack}>
      <Typo variant="heading">Registrar con factura</Typo>
      <Typo variant="body" color={colors.textSecondary}>
        Toma una foto o elige una que ya tengas.
      </Typo>

      <View style={styles.actions}>
        <Button label="Tomar foto" onPress={onCamera} accessibilityLabel="Tomar foto de la factura" />
        <Button
          label="Elegir de fotos"
          variant="secondary"
          onPress={onGallery}
          accessibilityLabel="Elegir una foto de la galería"
        />
      </View>

      {/* §3: "No prometer precisión absoluta." */}
      <Typo variant="caption" color={colors.textSecondary}>
        Fino intentará identificar el total, la fecha y el comercio. Siempre vas a poder revisarlo antes de
        guardar.
      </Typo>

      <Card tone="secondary" style={styles.privacyCard}>
        <View style={styles.inline}>
          <Ionicons name="lock-closed-outline" size={16} color={colors.text} />
          <Typo variant="bodyStrong">La foto no sale de tu teléfono</Typo>
        </View>
        <Typo variant="caption" color={colors.textSecondary}>
          Fino lee la factura aquí mismo, sin enviarla a ningún servidor.
        </Typo>
      </Card>
    </View>
  );
}

/** §29: estados reales, sin alargar la espera con una animación inventada. */
function Analyzing({ imageUri }: { imageUri: string | null }) {
  return (
    <View style={styles.stack}>
      <Typo variant="heading">Analizando factura</Typo>

      {imageUri ? <Image source={{ uri: imageUri }} style={styles.preview} resizeMode="cover" /> : null}

      <View style={styles.steps}>
        <Step label="Imagen preparada" done />
        <Step label="Leyendo el texto" done={false} />
        <Step label="Buscando el total" done={false} />
      </View>
    </View>
  );
}

function Step({ label, done }: { label: string; done: boolean }) {
  return (
    <View style={styles.inline}>
      <Ionicons
        name={done ? 'checkmark-circle' : 'ellipse-outline'}
        size={18}
        color={done ? colors.accentSecondary : colors.textSecondary}
      />
      <Typo variant="caption" color={done ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
    </View>
  );
}

/** §30 y §4: no pudimos leerla, o falta un permiso. Siempre con salida. */
function Failure({
  state,
  onRetry,
  onGallery,
  onManual,
}: {
  state: ReturnType<typeof useReceiptScan>['state'];
  onRetry: () => void;
  onGallery: () => void;
  onManual: () => void;
}) {
  if (state.unavailable) {
    return (
      <View style={styles.stack}>
        <Typo variant="heading">El escaneo todavía no está disponible</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Esta versión de Fino no incluye el lector de facturas. Mientras tanto puedes registrar el gasto a
          mano.
        </Typo>
        <Button label="Registrar manualmente" onPress={onManual} />
      </View>
    );
  }

  if (state.failure === ErrorReason.PermissionDenied) {
    return (
      <View style={styles.stack}>
        <Typo variant="heading">Fino necesita la cámara</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Puedes activarlo después en Configuración o elegir una foto de tu galería.
        </Typo>
        <View style={styles.actions}>
          <Button label="Elegir foto" onPress={onGallery} />
          <Button label="Ir a configuración" variant="secondary" onPress={() => void Linking.openSettings()} />
        </View>
      </View>
    );
  }

  const cropped = state.failure === ReceiptWarning.PossiblyCropped;

  return (
    <View style={styles.stack}>
      <Typo variant="heading">
        {cropped ? 'No vemos el total de la factura' : 'No pudimos leer bien esta factura'}
      </Typo>
      <Typo variant="body" color={colors.textSecondary}>
        {cropped
          ? 'Prueba tomando una foto donde aparezca la parte de abajo, que es donde suele estar el total.'
          : 'A veces ayuda más luz, o apoyar la factura en una superficie plana.'}
      </Typo>

      <View style={styles.actions}>
        <Button label="Tomar otra foto" onPress={onRetry} />
        <Button label="Elegir otra imagen" variant="secondary" onPress={onGallery} />
        <Button label="Registrar manualmente" variant="ghost" onPress={onManual} />
      </View>
    </View>
  );
}

/** §19: revisar antes de guardar. Nunca se crea el movimiento solo (§51). */
function Review({
  state,
  saving,
  onSave,
  onRetry,
}: {
  state: ReturnType<typeof useReceiptScan>['state'];
  saving: boolean;
  onSave: () => void;
  onRetry: () => void;
}) {
  const router = useRouter();
  const extraction = state.extraction;
  const draft = state.draft;

  if (!extraction || !draft) {
    return null;
  }

  const match = state.bankMatch;
  const lowDateConfidence = extraction.fieldConfidence.date < 0.5;

  return (
    <View style={styles.stack}>
      <Typo variant="heading">Revisa antes de guardar</Typo>

      <View style={styles.reviewHeader}>
        {state.imageUri ? (
          <Image source={{ uri: state.imageUri }} style={styles.thumb} resizeMode="cover" />
        ) : null}

        <View style={styles.flex}>
          <Typo variant="bodyStrong" numberOfLines={1}>
            {extraction.merchantName ?? 'Sin comercio'}
          </Typo>
          <Typo variant="display">{formatCurrency(draft.amount)}</Typo>
        </View>
      </View>

      {/* §22 y §50: el gasto puede estar ya en el banco. No se crea otro. */}
      {match?.best && match.best.confidence === MatchConfidence.High ? (
        <Card tone="secondary" style={styles.stackTight}>
          <Typo variant="bodyStrong">Este gasto parece estar ya en {match.best.transaction.accountAlias}</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {formatCurrency(match.best.transaction.amount)} ·{' '}
            {formatShortDate(new Date(match.best.transaction.transactionDate))}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Guardarlo igualmente contaría el gasto dos veces.
          </Typo>
        </Card>
      ) : null}

      {match?.ambiguous ? (
        <Card tone="secondary" style={styles.stackTight}>
          <Typo variant="bodyStrong">Hay varios movimientos que podrían ser este</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Encontramos {match.candidates.length} con el mismo monto. Revísalos en Movimientos antes de
            registrarlo por separado.
          </Typo>
        </Card>
      ) : null}

      {/* §26: puede que ya lo hayas anotado tú. */}
      {state.duplicates.length > 0 ? (
        <Card tone="secondary" style={styles.stackTight}>
          <Typo variant="bodyStrong">Puede que este gasto ya esté registrado</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Tienes un movimiento de {formatCurrency(state.duplicates[0]!.transaction.amount)} el mismo día.
          </Typo>
        </Card>
      ) : null}

      <Card style={styles.stackTight}>
        <Field label="Total" value={formatCurrency(draft.amount)} />
        <Field
          label="Fecha"
          value={draft.occurredAt ? formatShortDate(new Date(draft.occurredAt)) : 'Hoy'}
          // §18: la confianza no se muestra como porcentaje, se muestra como duda.
          hint={lowDateConfidence ? 'Toca para verificar' : undefined}
        />
        {extraction.tax !== null ? <Field label="IVA" value={formatCurrency(extraction.tax)} /> : null}
        <Field
          label="Pagado con"
          value={draft.financialAccountId ? 'Efectivo' : 'Elegir cuenta'}
          hint={draft.financialAccountId ? undefined : 'Toca para elegir'}
        />
      </Card>

      {/* §6: la foto era pequeña. Se avisa, no se bloquea. */}
      {state.lowQuality ? (
        <Typo variant="caption" color={colors.textSecondary}>
          La foto tiene poca resolución. Si algo no cuadra, prueba con otra.
        </Typo>
      ) : null}

      <View style={styles.actions}>
        <Button label="Guardar" onPress={onSave} loading={saving} accessibilityLabel="Guardar el movimiento" />
        <Button
          label="Más detalles"
          variant="secondary"
          onPress={() => router.push('/movimiento/nuevo')}
          accessibilityLabel="Abrir el formulario completo"
        />
        <Button label="Tomar otra foto" variant="ghost" onPress={onRetry} />
      </View>
    </View>
  );
}

function Field({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <View style={styles.fieldRow}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <View style={styles.fieldValue}>
        <Typo variant="bodyStrong">{value}</Typo>
        {hint ? (
          <Typo variant="caption" color={colors.warning}>
            {hint}
          </Typo>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  back: { alignSelf: 'flex-start', paddingVertical: spacing.sm },
  stack: { gap: spacing.md },
  stackTight: { gap: spacing.xs },
  actions: { gap: spacing.sm },
  inline: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  flex: { flex: 1 },
  privacyCard: { gap: spacing.xs },
  steps: { gap: spacing.sm },
  preview: { width: '100%', height: 200, borderRadius: radius.lg, backgroundColor: colors.surfaceSecondary },
  thumb: { width: 64, height: 64, borderRadius: radius.md, backgroundColor: colors.surfaceSecondary },
  reviewHeader: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  fieldRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: spacing.xs,
  },
  fieldValue: { alignItems: 'flex-end' },
});
