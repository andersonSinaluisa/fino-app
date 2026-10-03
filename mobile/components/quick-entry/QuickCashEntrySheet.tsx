import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Pressable,
  StyleSheet,
  View,
} from 'react-native';
import { useRouter } from 'expo-router';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing, typography } from '../../theme';
import { Typo } from '../ui/Typo';
import { Button } from '../ui/Button';
import { useToast } from '../ui/Toast';
import { KeyboardAwareScrollView } from '../ui/KeyboardAwareScrollView';
import { KeyboardSpacer } from '../ui/KeyboardSpacer';
import { AmountKeypad, applyKey, type KeypadKey } from './AmountKeypad';
import { TypeToggle } from './TypeToggle';
import { SmartEntryInput } from './SmartEntryInput';
import { SuggestionChips } from './SuggestionChips';
import { VoiceButton, voiceStatusText } from './VoiceButton';
import { SuggestionEditorSheet } from './SuggestionEditorSheet';
import {
  useCategories,
  useCreateQuickTransaction,
  useQuickEntryBootstrap,
  useUndoQuickTransaction,
} from '../../hooks/queries';
import { useQuickEntryStore } from '../../store/quickEntryStore';
import { useConnectivityStore } from '../../store/connectivityStore';
import { useAuthStore } from '../../store/authStore';
import { useOfflineQueueStore } from '../../store/offlineQueueStore';
import { useVoiceEntry } from '../../hooks/useVoiceEntry';
import { parseNaturalEntry, type ParsedEntry } from '../../utils/quickEntry/parseNaturalEntry';
import { evaluateExpression, looksLikeExpression } from '../../utils/quickEntry/safeCalculator';
import { createRequestId } from '../../utils/quickEntry/requestId';
import { findCategoryById, resolveCategory } from '../../utils/quickEntry/resolveCategory';
import { formatCurrency } from '../../utils/format';
import {
  AnalyticsEvent,
  ErrorReason,
  TransactionDirection,
  toDurationBucket,
  track,
  trackOnce,
  type QuickEntryMethod,
} from '../../services/analytics';
import { ApiError } from '../../services/apiClient';
import { devLog } from '../../services/devLog';
import type { CreateQuickTransactionRequest, QuickEntrySuggestion } from '../../types/api';

/**
 * §2: el bottom sheet de registro rápido.
 *
 * La jerarquía que impone el §2 no es decorativa; es lo que hace que el caso normal
 * dure 1-3 segundos. De arriba abajo: (1) monto, (2) Guardar, (3) gasto/ingreso,
 * (4) entrada inteligente, (5) frecuentes, (6) más detalles. Los cuatro últimos
 * están presentes pero deliberadamente callados -- si compitieran visualmente con
 * el monto, la persona tendría que elegir por dónde empezar, y elegir cuesta más
 * tiempo que teclear.
 *
 * Lo que este componente NO hace: no crea movimientos por su cuenta. Todos sus
 * caminos (teclado, texto natural, voz, frecuentes) terminan en el mismo
 * `useCreateQuickTransaction`, que llama al mismo endpoint, que llama al mismo caso
 * de uso del servidor. Añadir un quinto camino no debería añadir una quinta forma
 * de guardar.
 */

/** §3: "$0.00" es el estado inicial y el campo está vacío por dentro. */
const EMPTY_AMOUNT = '';

function formatAmountDisplay(amountText: string): string {
  if (amountText.length === 0) {
    return '0.00';
  }

  // Mientras se escribe se muestra EXACTAMENTE lo tecleado ("5.", "5.4"): formatear
  // a dos decimales en cada pulsación haría saltar el cursor y pelearía con quien
  // está escribiendo. El formato bonito llega al guardar.
  return amountText;
}

/**
 * El contenido del sheet, separado de la cáscara `<Modal>` que lo presenta.
 *
 * La separación no es cosmética: un `<Modal>` es una ventana del sistema operativo,
 * no un nodo normal del árbol de React, así que todo lo que vive dentro queda fuera
 * del alcance de un test de componente. Con el contenido en su propio componente
 * exportado, las pruebas del §39 (guardar, deshacer, doble toque, fallo de la API)
 * ejercitan el teclado, el selector, los hooks y el toast DE VERDAD, en vez de
 * tener que simularlos.
 */
export function QuickCashEntryContent() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const toast = useToast();

  const visible = useQuickEntryStore((state) => state.visible);
  const openedAt = useQuickEntryStore((state) => state.openedAt);
  const source = useQuickEntryStore((state) => state.source);
  const startWithVoice = useQuickEntryStore((state) => state.startWithVoice);
  const draft = useQuickEntryStore((state) => state.draft);
  const setDraft = useQuickEntryStore((state) => state.setDraft);
  const close = useQuickEntryStore((state) => state.close);
  const handOffToFullForm = useQuickEntryStore((state) => state.handOffToFullForm);
  const consumeVoiceIntent = useQuickEntryStore((state) => state.consumeVoiceIntent);

  const { data: bootstrap } = useQuickEntryBootstrap(visible);
  const { data: categories } = useCategories();
  const create = useCreateQuickTransaction();
  const undo = useUndoQuickTransaction();
  // Modo offline, "registrar sin señal": ver services/offlineStorage.ts y
  // hooks/useOfflineSync.ts.
  const isOnline = useConnectivityStore((state) => state.isOnline);
  const userId = useAuthStore((state) => state.user?.id);
  const enqueueOffline = useOfflineQueueStore((state) => state.enqueue);
  const removeOffline = useOfflineQueueStore((state) => state.remove);

  const [smartText, setSmartText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<QuickEntrySuggestion | null>(null);

  /**
   * §36: el identificador de idempotencia se genera UNA vez por intento de
   * guardado, no por petición. Si el primer envío falla por red y la persona toca
   * Reintentar, se manda el mismo id: si aquel envío sí había llegado, el servidor
   * devuelve el movimiento que ya creó en vez de cobrar dos veces.
   */
  const attemptId = useRef<string | null>(null);

  const parsed: ParsedEntry | null = useMemo(
    () => (smartText.trim().length > 0 ? parseNaturalEntry(smartText) : null),
    [smartText],
  );

  const parsedCategory = useMemo(
    () => resolveCategory(parsed?.categoryCode ?? null, categories),
    [parsed?.categoryCode, categories],
  );

  /**
   * §16: calidad del parser local.
   *
   * `fieldsParsed` cuenta CUÁNTOS de los tres campos (monto, fecha,
   * categoría) reconoció -- 0 a 3. No viaja ni el texto, ni el monto, ni el
   * código de categoría: solo cuántas piezas entendió, que es lo que dice si
   * el parser sirve o si la gente tiene que corregirlo siempre.
   *
   * Se mide al terminar de escribir (`debouncedSmartText`), no en cada
   * pulsación, porque si no cada letra generaría un evento.
   */
  const parseOutcome = useMemo(() => {
    if (!parsed || smartText.trim().length === 0) {
      return null;
    }

    if (parsed.amount === null) {
      return { ok: false as const, fieldsParsed: 0 };
    }

    const fieldsParsed =
      1 + (parsed.occurredAt !== null ? 1 : 0) + (parsed.categoryCode !== null ? 1 : 0);

    return { ok: true as const, fieldsParsed };
  }, [parsed, smartText]);

  const draftCategory = useMemo(
    () => findCategoryById(draft.categoryId, categories),
    [draft.categoryId, categories],
  );

  const handleTranscript = useCallback(
    (transcript: string) => {
      // §13: la voz no es un camino aparte. Su transcripción entra por el MISMO
      // campo de texto natural y la interpreta el MISMO parser, así que "gasté seis
      // dólares en almuerzo" y teclear "6 almuerzo" acaban en el mismo sitio.
      setSmartText(transcript);
      setError(null);
    },
    [],
  );

  const voice = useVoiceEntry({ onTranscript: handleTranscript });

  // §13: mantener pulsado el "+" abre el sheet ya escuchando.
  useEffect(() => {
    if (visible && startWithVoice && voice.available) {
      consumeVoiceIntent();
      void voice.start();
    } else if (visible && startWithVoice) {
      consumeVoiceIntent();
    }
  }, [visible, startWithVoice, voice, consumeVoiceIntent]);

  useEffect(() => {
    if (visible) {
      track(AnalyticsEvent.QuickEntryOpened, { source });
    } else {
      setSmartText('');
      setError(null);
      attemptId.current = null;
      voice.cancel();
    }
    // `voice` cambia de identidad en cada render; incluirlo reiniciaría el efecto
    // constantemente. Lo que importa aquí es el cambio de `visible`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible, source]);

  /**
   * §20: el monto puede ser una expresión ("5.50 + 2.25"). Se evalúa aquí y no al
   * guardar para que el total se vea antes de tocar Guardar.
   */
  const amountFromKeypad = useMemo(() => {
    const evaluated = evaluateExpression(draft.amountText);
    return evaluated?.value ?? null;
  }, [draft.amountText]);

  // El texto natural manda sobre el teclado cuando hay algo escrito ahí: si alguien
  // escribió "8 uber ayer", ese 8 es lo que quiere guardar.
  const usingSmartText = parsed !== null && parsed.canSave;
  const effectiveAmount = usingSmartText ? parsed.amount : amountFromKeypad;
  const effectiveDirection = usingSmartText ? parsed.direction : draft.direction;
  const canSave = effectiveAmount !== null && effectiveAmount > 0 && !create.isPending;

  const handleKey = useCallback(
    (key: KeypadKey) => {
      setError(null);
      setDraft({ amountText: applyKey(draft.amountText, key) });
    },
    [draft.amountText, setDraft],
  );

  /**
   * Modo offline, "registrar sin señal": el movimiento ya quedó capturado
   * localmente -- por eso cierra el sheet y muestra el mismo toast con
   * Deshacer que un guardado real, en vez de dejar el sheet abierto con un
   * error. La diferencia deliberada con el toast normal: el mensaje NUNCA
   * dice "registrado" a secas, para no insinuar que el saldo/resumen ya lo
   * refleja -- no lo hace hasta que useOfflineSync lo mande de verdad.
   * Deshacer aquí no llama al servidor (nunca llegó a él): solo quita la
   * entrada de la cola local.
   */
  const saveOffline = useCallback(
    async (
      body: CreateQuickTransactionRequest,
      clientRequestId: string,
      options: { amount: number; direction: 'Income' | 'Expense'; method: QuickEntryMethod },
    ) => {
      if (!userId) {
        // No debería pasar -- el sheet solo se abre con sesión activa -- pero
        // encolar un movimiento sin dueño sería peor que perder este intento:
        // se trata como el error genérico de siempre en vez de guardar algo
        // que después nadie podría sincronizar de forma segura.
        setError('No pudimos guardar el movimiento.');
        return;
      }

      const entry = await enqueueOffline(body, clientRequestId, userId);

      track(AnalyticsEvent.QuickEntryQueuedOffline, {
        source,
        entryMode: options.method,
        transactionType:
          options.direction === 'Income' ? TransactionDirection.Income : TransactionDirection.Expense,
      });

      close();

      toast.show({
        message: `${formatCurrency(options.amount)} guardado -- se sincronizará cuando tengas señal`,
        action: {
          label: 'Deshacer',
          onPress: () => {
            track(AnalyticsEvent.QuickEntryUndoOffline, { entryMode: options.method });
            void removeOffline(entry.localId);
          },
        },
      });
    },
    [close, enqueueOffline, openedAt, removeOffline, source, toast, userId],
  );

  const save = useCallback(
    async (options: {
      amount: number;
      direction: 'Income' | 'Expense';
      description?: string | null;
      categoryId?: string | null;
      occurredAt?: string | null;
      financialAccountId?: string | null;
      method: QuickEntryMethod;
    }) => {
      if (create.isPending) {
        // §36: el guardado ya está en vuelo. El botón también se deshabilita, pero
        // esta guarda cubre el toque que entra en el mismo frame.
        return;
      }

      attemptId.current ??= createRequestId();
      const clientRequestId = attemptId.current;

      const body: CreateQuickTransactionRequest = {
        amount: options.amount,
        direction: options.direction,
        description: options.description ?? null,
        categoryId: options.categoryId ?? undefined,
        occurredAt: options.occurredAt ?? null,
        financialAccountId: options.financialAccountId ?? null,
        clientRequestId,
      };

      // Modo offline: sin señal, ni se intenta -- evita los 20s de timeout de
      // apiClient (config.requestTimeoutMs) para terminar en el mismo sitio.
      // El envío real lo hace useOfflineSync en cuanto vuelva la conexión,
      // con este mismo clientRequestId (§36), así que nunca duplica el
      // movimiento aunque este intento y el de la cola coincidan.
      if (!isOnline) {
        await saveOffline(body, clientRequestId, options);
        return;
      }

      try {
        const created = await create.mutateAsync(body);

        // §38: la métrica TIME_TO_CASH_ENTRY. Viaja la vía y el TRAMO de
        // tiempo, jamás el monto, la descripción ni la categoría.
        //
        // Antes esto mandaba `elapsedMs` exacto. Un milisegundo no hace falta
        // para saber si el registro baja de 3 segundos, y junto a la hora del
        // evento es casi un identificador por persona: ahora va bucketizado.
        track(AnalyticsEvent.QuickEntrySaved, {
          source,
          entryMode: options.method,
          transactionType:
            options.direction === 'Income' ? TransactionDirection.Income : TransactionDirection.Expense,
          categorySource: options.categoryId ? 'manual' : 'auto',
          offline: false,
          durationBucket: openedAt ? toDurationBucket(Date.now() - openedAt) : undefined,
        });

        // §30: el primer registro de efectivo de esta persona, una sola vez.
        void trackOnce(AnalyticsEvent.FirstCashEntry, { entryMode: options.method });

        close();

        // §6: el toast con Deshacer. Es lo que permite no pedir confirmación antes
        // de guardar: la salida está a un toque durante unos segundos.
        toast.show({
          message: `${formatCurrency(options.amount)} registrado`,
          action: {
            label: 'Deshacer',
            onPress: () => {
              track(AnalyticsEvent.QuickEntryUndo, { entryMode: options.method });
              undo.mutate(created.id, {
                onError: () =>
                  toast.show({ message: 'No pudimos deshacerlo. Inténtalo otra vez.', tone: 'error' }),
              });
            },
          },
        });
      } catch (caught) {
        // Un error de RED puro -- sin respuesta del servidor, ApiError status 0,
        // ver apiClient.ts's networkProblem -- se trata igual que estar offline
        // desde el principio: se encola en vez de exigir "Reintentar" a mano. Un
        // rechazo real del servidor (400, una sesión que de verdad expiró) sigue
        // siendo un error de verdad y no se encola.
        if (caught instanceof ApiError && caught.status === 0) {
          await saveOffline(body, clientRequestId, options);
          return;
        }

        // §34: "si falla creación, NO cerrar el sheet. Mantener todo lo escrito."
        // El sheet sigue abierto con el monto puesto y aparece Reintentar; el
        // attemptId se conserva a propósito para que reintentar sea idempotente.
        track(AnalyticsEvent.QuickEntryFailed, {
          entryMode: options.method,
          reason: caught instanceof ApiError ? ErrorReason.ServerError : ErrorReason.NetworkError,
        });

        // Un mensaje genérico esconde la causa: la primera versión decía siempre
        // "No pudimos guardar el movimiento" mientras el servidor respondía 400
        // explicando exactamente qué campo estaba mal. Si el backend manda un
        // motivo legible, se muestra.
        const detail = caught instanceof ApiError ? caught.message : null;
        setError(detail ?? 'No pudimos guardar el movimiento.');
        devLog('quickEntry', 'save_failed', {
          status: caught instanceof ApiError ? caught.status : null,
        });

        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning).catch(() => undefined);
      }
    },
    [close, create, isOnline, openedAt, saveOffline, source, toast, undo],
  );

  const handleSave = useCallback(() => {
    if (effectiveAmount === null) {
      return;
    }

    if (usingSmartText && parsed) {
      track(AnalyticsEvent.SmartEntryUsed, { source });

      if (parseOutcome?.ok) {
        track(AnalyticsEvent.SmartEntryParsed, { fieldsParsed: parseOutcome.fieldsParsed });
      } else {
        track(AnalyticsEvent.SmartEntryFailed, { reason: ErrorReason.NotRecognized });
      }

      void save({
        amount: effectiveAmount,
        direction: parsed.direction,
        description: parsed.description,
        // Si el parser no resolvió una categoría real de esta persona, se manda sin
        // categoría y decide el motor de reglas del servidor (§10, prioridades 1-2).
        categoryId: parsedCategory?.id ?? undefined,
        occurredAt: parsed.occurredAt,
        method: 'smart_text',
      });
      return;
    }

    void save({
      amount: effectiveAmount,
      direction: draft.direction,
      description: draft.description.trim().length > 0 ? draft.description.trim() : null,
      categoryId: draft.categoryId ?? undefined,
      occurredAt: draft.occurredAt,
      financialAccountId: draft.financialAccountId,
      method: 'keypad',
    });
  }, [draft, effectiveAmount, parseOutcome, parsed, parsedCategory, save, source, usingSmartText]);

  /** §17: un toque registra. Sin confirmación, porque Deshacer existe. */
  const handleUseSuggestion = useCallback(
    (suggestion: QuickEntrySuggestion, kind: 'frequent' | 'recent') => {
      if (!suggestion.amountIsReliable) {
        // §17: "si existe un typicalAmount confiable, usar ese monto." Cuando no lo
        // hay, se rellena todo menos el monto en vez de inventar una cifra.
        setDraft({
          amountText: EMPTY_AMOUNT,
          direction: suggestion.direction,
          description: suggestion.label,
          categoryId: suggestion.categoryId,
          financialAccountId: suggestion.financialAccountId,
        });
        setSmartText('');
        return;
      }

      track(AnalyticsEvent.FrequentEntryUsed, { kind });
      void save({
        amount: suggestion.typicalAmount,
        direction: suggestion.direction,
        description: suggestion.label,
        categoryId: suggestion.categoryId,
        financialAccountId: suggestion.financialAccountId,
        method: kind === 'frequent' ? 'frequent' : 'recent',
      });
    },
    [save, setDraft],
  );

  /** §22: abrir el formulario completo SIN perder lo ya capturado. */
  const openFullForm = useCallback(() => {
    track(AnalyticsEvent.QuickEntryDetailsOpened, { source });

    if (usingSmartText && parsed) {
      // Lo que el parser entendió pasa al formulario ya relleno: el ejemplo literal
      // del §22 es escribir "8 uber ayer" y ver el formulario con tipo, monto,
      // descripción, categoría, cuenta y fecha puestos.
      setDraft({
        amountText: String(parsed.amount ?? ''),
        direction: parsed.direction,
        description: parsed.description ?? '',
        categoryId: parsedCategory?.id ?? null,
        occurredAt: parsed.occurredAt,
        dateLabel: parsed.dateLabel,
      });
    }

    handOffToFullForm();
    router.push('/movimiento/nuevo');
  }, [handOffToFullForm, parsed, parsedCategory, router, setDraft, usingSmartText]);

  const suggestions = bootstrap?.frequent ?? [];
  const recents = bootstrap?.recent ?? [];

  return (
    <>
      {/* El teclado empuja la hoja hacia arriba en iOS y en Android (edge-to-edge
          incluido): KeyboardSpacer ocupa exactamente lo que el teclado tapa. */}
      <View style={styles.sheetWrapper}>
        <View style={[styles.sheet, { paddingBottom: insets.bottom + spacing.lg }]}>
          <View style={styles.handle} />

          <KeyboardAwareScrollView
            keyboardSpacer={false}
            keyboardShouldPersistTaps="handled"
            showsVerticalScrollIndicator={false}
            contentContainerStyle={styles.content}
          >
            {/* §41: "Registrar efectivo", no "Nueva transacción manual". */}
            <Typo variant="bodyStrong" align="center">
              Registrar efectivo
            </Typo>

            {/* JERARQUÍA 1: el monto. Es lo más grande de la pantalla, con
                diferencia, porque es lo único que hace falta escribir. */}
            <View style={styles.amountRow} accessibilityLiveRegion="polite">
              <Typo
                style={[
                  styles.amount,
                  effectiveDirection === 'Income' ? styles.amountIncome : styles.amountExpense,
                ]}
                tabular
                accessibilityLabel={`Monto ${formatCurrency(effectiveAmount ?? 0)}`}
                testID="quick-entry-amount"
              >
                {effectiveDirection === 'Income' ? '+' : ''}$
                {usingSmartText
                  ? (parsed.amount ?? 0).toFixed(2)
                  : formatAmountDisplay(draft.amountText)}
              </Typo>
            </View>

            {/* §20: el total de una expresión, mientras se escribe. */}
            {looksLikeExpression(draft.amountText) && amountFromKeypad !== null && !usingSmartText ? (
              <Typo variant="caption" color={colors.textSecondary} align="center">
                {draft.amountText} = {formatCurrency(amountFromKeypad)}
              </Typo>
            ) : null}

            {/* §3: la línea que dice qué se va a guardar si no se toca nada más.
                Está aquí, discreta, para que los valores por defecto sean visibles
                sin ser un formulario. */}
            <Typo variant="caption" color={colors.textSecondary} align="center">
              {[
                bootstrap && bootstrap.cashAccountId ? 'Efectivo' : 'Efectivo',
                parsed?.dateLabel && parsed.dateLabel !== 'Hoy' ? parsed.dateLabel : 'Ahora',
                parsedCategory?.name ?? draftCategory?.name ?? 'Sin categoría',
              ].join(' · ')}
            </Typo>

            {/* JERARQUÍA 3: gasto/ingreso. */}
            <TypeToggle
              value={effectiveDirection}
              onChange={(direction) => setDraft({ direction })}
              disabled={create.isPending || usingSmartText}
            />

            {!usingSmartText ? (
              <AmountKeypad onKey={handleKey} disabled={create.isPending} />
            ) : null}

            {/* §34: el error no cierra nada ni borra nada. */}
            {error ? (
              <View style={styles.error} accessibilityLiveRegion="assertive">
                <Ionicons name="alert-circle" size={18} color={colors.danger} />
                <Typo variant="body" color={colors.danger} style={styles.errorText}>
                  {error}
                </Typo>
              </View>
            ) : null}

            {/* JERARQUÍA 2: Guardar. */}
            <Button
              label={error ? 'Reintentar' : 'Guardar'}
              onPress={handleSave}
              disabled={!canSave}
              loading={create.isPending}
              loadingLabel="Guardando..."
              softDisabled
              testID="quick-entry-save"
              accessibilityLabel={
                effectiveAmount !== null
                  ? `Guardar ${formatCurrency(effectiveAmount)}`
                  : 'Guardar. Escribe primero un monto.'
              }
            />

            {/* JERARQUÍA 4: entrada inteligente. */}
            <SmartEntryInput
              value={smartText}
              onChangeText={(text) => {
                setSmartText(text);
                setError(null);
              }}
              parsed={parsed}
              category={parsedCategory}
              onSubmit={handleSave}
              disabled={create.isPending}
              voiceStatus={voice.available ? voiceStatusText(voice.status, voice.partial, voice.message) : null}
              voiceStatusIsError={voice.status === 'idle' && voice.message !== null}
              accessory={
                voice.available ? (
                  <VoiceButton
                    status={voice.status}
                    onStart={() => void voice.start()}
                    onStop={voice.stop}
                    disabled={create.isPending}
                  />
                ) : null
              }
            />

            {/* JERARQUÍA 5: frecuentes (o recientes, cuando aún no hay hábitos). */}
            {suggestions.length > 0 ? (
              <SuggestionChips
                title="Frecuentes"
                suggestions={suggestions}
                onUse={(suggestion) => handleUseSuggestion(suggestion, 'frequent')}
                onEdit={setEditing}
                disabled={create.isPending}
              />
            ) : (
              <SuggestionChips
                title="Recientes"
                suggestions={recents}
                onUse={(suggestion) => handleUseSuggestion(suggestion, 'recent')}
                onEdit={setEditing}
                disabled={create.isPending}
              />
            )}

            {/* §2 del escaneo de facturas: "Escanear factura" tiene que ser una
                opción de PRIMER NIVEL, y aquí es donde de verdad lo es.

                Deliberadamente NO se convirtió el "+" en un menú (Efectivo /
                Factura / Importar): eso añadiría un toque delante del camino
                rápido y rompería la regla de que registrar efectivo tome
                entre 1 y 3 segundos, que es la métrica que mide
                quick_entry_saved.durationBucket. El "+" sigue abriendo
                directo el teclado; la factura vive aquí dentro, a un toque. */}
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Escanear factura"
              onPress={() => {
                track(AnalyticsEvent.ReceiptScanOpened, { source });
                close();
                router.push('/factura');
              }}
              disabled={create.isPending}
              style={({ pressed }) => [styles.moreDetails, pressed ? styles.pressed : null]}
            >
              <Ionicons name="scan-outline" size={16} color={colors.textSecondary} />
              <Typo variant="body" color={colors.textSecondary}>
                Escanear factura
              </Typo>
            </Pressable>

            {/* JERARQUÍA 6: más detalles. Un enlace, no un botón: existe para el
                caso raro y no debe competir con Guardar. */}
            <Pressable
              accessibilityRole="link"
              accessibilityLabel="Más detalles"
              onPress={openFullForm}
              disabled={create.isPending}
              style={({ pressed }) => [styles.moreDetails, pressed ? styles.pressed : null]}
            >
              <Typo variant="body" color={colors.textSecondary}>
                Más detalles
              </Typo>
              <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
            </Pressable>

            {create.isPending ? (
              <View style={styles.savingHint}>
                <ActivityIndicator size="small" color={colors.textSecondary} />
              </View>
            ) : null}
          </KeyboardAwareScrollView>
        </View>
        <KeyboardSpacer />
      </View>

      {/* §18: mini editor de un frecuente. */}
      <SuggestionEditorSheet
        suggestion={editing}
        categories={categories ?? []}
        onClose={() => setEditing(null)}
        onSave={(amount, categoryId) => {
          const target = editing;
          setEditing(null);

          if (!target) {
            return;
          }

          void save({
            amount,
            direction: target.direction,
            description: target.label,
            categoryId,
            financialAccountId: target.financialAccountId,
            method: 'frequent',
          });
        }}
      />
    </>
  );
}

/**
 * La cáscara. Presenta el contenido dentro de un `<Modal>` y lo DESMONTA al
 * cerrarlo, de modo que cada apertura empieza limpia sin que el contenido tenga que
 * acordarse de reiniciarse a sí mismo.
 */
export function QuickCashEntrySheet() {
  const visible = useQuickEntryStore((state) => state.visible);
  const close = useQuickEntryStore((state) => state.close);

  return (
    <Modal
      visible={visible}
      transparent
      // §32: 200-300 ms. `slide` del sistema entra en ese rango y es el que ya usa
      // SelectSheet, así que el registro rápido no se siente como otra app.
      animationType="slide"
      onRequestClose={close}
      statusBarTranslucent
    >
      {/* El contenedor es quien tiene el `flex: 1` y quien empuja el sheet abajo.
          Antes el backdrop era un hermano con `flex: 1` que se llevaba TODA la
          altura, así que el sheet se salía por arriba de la pantalla y el monto
          aparecía cortado por la mitad. El backdrop ahora va superpuesto, sin
          ocupar espacio en el layout. */}
      <View style={styles.root}>
        <Pressable
          style={StyleSheet.absoluteFill}
          onPress={close}
          accessibilityRole="button"
          accessibilityLabel="Cerrar"
        />

        <QuickCashEntryContent />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    // El sheet se apoya en el borde inferior; el resto es fondo tocable para cerrar.
    justifyContent: 'flex-end',
    backgroundColor: colors.overlay,
  },
  sheetWrapper: {
    // Sin `flex: 1`: el contenedor mide lo que miden la hoja y el espacio del
    // teclado, no se estira. De estirarse, la hoja volvería a subir hasta arriba.
    justifyContent: 'flex-end',
    flexShrink: 1,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.lg,
    maxHeight: '92%',
    flexShrink: 1,
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.md,
  },
  content: {
    gap: spacing.lg,
    paddingBottom: spacing.lg,
  },
  amountRow: {
    alignItems: 'center',
    paddingVertical: spacing.sm,
  },
  amount: {
    fontSize: typography.display.fontSize,
    // Un poco más de interlineado que el de la escala tipográfica: con peso 700 y
    // 44px, iOS recorta las cifras cuando lineHeight va justo al tamaño de fuente.
    lineHeight: typography.display.lineHeight + 8,
    fontWeight: '700',
    letterSpacing: -1.4,
    // Evita que un tipo de letra con ascendentes altos se coma el borde superior.
    paddingTop: 2,
  },
  // §5: el gasto es tinta, no rojo. Gastar dinero es lo normal, no una alarma.
  amountExpense: {
    color: colors.text,
  },
  amountIncome: {
    color: colors.success,
  },
  error: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: 'rgba(216, 102, 91, 0.10)',
  },
  errorText: {
    flex: 1,
  },
  moreDetails: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs,
    minHeight: 44,
  },
  pressed: {
    opacity: 0.6,
  },
  savingHint: {
    alignItems: 'center',
  },
});
