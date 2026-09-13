import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import * as DocumentPicker from 'expo-document-picker';
import { File } from 'expo-file-system';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, Screen, SectionHeader, Typo } from '../../components/ui';
import { api } from '../../services/endpoints';
import { ApiError } from '../../services/apiClient';
import { devLog, serializeError } from '../../services/devLog';
import {
  useAccount,
  useCancelImport,
  useRefreshAfterImport,
  useSkipOnboarding,
  useWithdrawalScan,
} from '../../hooks/queries';
import { useAuthStore } from '../../store/authStore';
import { getBankImportConfig } from '../../lib/bankTutorials';
import { ImportProcessingReveal } from '../../components/onboarding/ImportProcessingReveal';
import { OnboardingTopBar } from '../../components/onboarding/OnboardingTopBar';
import { SkipOnboardingSheet } from '../../components/onboarding/SkipOnboardingSheet';
import {
  AnalyticsEvent,
  AnalyticsSource,
  ErrorReason,
  toCountBucket,
  toDurationBucket,
  toFileFormat,
  track,
  trackOnce,
} from '../../services/analytics';
import { formatCurrency, formatDayHeading } from '../../utils/format';
import { ACCEPTED_EXTENSIONS, extensionOf, guessMimeType } from '../../lib/importFileTypes';
import type { ImportPreview, ImportResult, ManualColumnMapping } from '../../types/api';

const LOG_TAG = 'importar';

type Step = 'pick' | 'working' | 'preview' | 'mapping' | 'done';

type PickedAsset = { uri: string; name: string; mimeType: string; size: number | null };

/** One raw column can carry at most one of these roles. */
type ColumnRole = 'date' | 'description' | 'debit' | 'credit' | 'amount' | 'reference';

const ROLE_LABELS: Record<ColumnRole, string> = {
  date: 'Fecha',
  description: 'Descripción',
  debit: 'Débito',
  credit: 'Crédito',
  amount: 'Monto (+/-)',
  reference: 'Referencia',
};

const ROLE_ORDER: ColumnRole[] = ['date', 'description', 'debit', 'credit', 'amount', 'reference'];

type RoleColumns = Record<ColumnRole, number | null>;

const EMPTY_ROLE_COLUMNS: RoleColumns = {
  date: null,
  description: null,
  debit: null,
  credit: null,
  amount: null,
  reference: null,
};

const ACCEPTED_TYPES = [
  'text/csv',
  'text/comma-separated-values',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'text/plain',
];

// ACCEPTED_EXTENSIONS (lib/importFileTypes.ts) is the real accept/reject
// gate, not ACCEPTED_TYPES above -- see that file for why.
type ResolvedPick =
  | { ok: true; asset: PickedAsset }
  | { ok: false; errorMessage: string };

/**
 * `copyToCacheDirectory: true` is supposed to hand back a plain local file,
 * copied into the app's own sandbox, before the picker's promise resolves --
 * so by this point `picked.assets[0].uri` should already point at real bytes
 * on disk regardless of where the file came from (iCloud placeholder, another
 * app's file provider, wherever). Some providers break that contract instead:
 * they report success but the "copy" is empty or never lands, which upload()
 * then can't tell apart from a plain network failure. Checking the copy here,
 * synchronously, with the real filesystem (not just trusting the picker's own
 * metadata) is what lets the error message name the actual problem.
 *
 * Web has no equivalent failure mode and no real `expo-file-system` File
 * implementation to check against (its web build is a stub -- calling
 * `.exists`/`.size` there would throw): the picker already hands back a
 * ready-to-use `Blob`/`File` (`rawAsset.file`) and a `blob:`/`data:` uri with
 * no separate native copy step, so web only validates size/extension.
 */
function resolvePickedFile(rawAsset: DocumentPicker.DocumentPickerAsset): ResolvedPick {
  devLog(LOG_TAG, 'resolve_start', {
    uri: rawAsset.uri,
    name: rawAsset.name,
    mimeType: rawAsset.mimeType,
    reportedSize: rawAsset.size,
    platform: Platform.OS,
  });

  const ext = extensionOf(rawAsset.name);
  if (!ext || !ACCEPTED_EXTENSIONS.includes(ext)) {
    devLog(LOG_TAG, 'resolve_failed', {
      reason: 'unsupported_extension',
      name: rawAsset.name,
      ext,
      mimeType: rawAsset.mimeType,
    });
    return { ok: false, errorMessage: 'Ese tipo de archivo no lo soportamos todavía. Sube un CSV o un XLSX.' };
  }

  if (Platform.OS === 'web') {
    const size = rawAsset.size ?? null;
    if (size === 0) {
      devLog(LOG_TAG, 'resolve_failed', { reason: 'empty_file', name: rawAsset.name });
      return {
        ok: false,
        errorMessage: 'Ese archivo está vacío. Descarga de nuevo el estado de cuenta e inténtalo otra vez.',
      };
    }
    const asset: PickedAsset = { uri: rawAsset.uri, name: rawAsset.name, mimeType: rawAsset.mimeType ?? 'text/csv', size };
    devLog(LOG_TAG, 'resolve_success', asset);
    return { ok: true, asset };
  }

  let local: File;
  try {
    local = new File(rawAsset.uri);
  } catch (constructError) {
    devLog(LOG_TAG, 'resolve_failed', {
      reason: 'local_file_handle_failed',
      uri: rawAsset.uri,
      name: rawAsset.name,
      ...serializeError(constructError),
    });
    return {
      ok: false,
      errorMessage:
        'No pudimos acceder a ese archivo. Pruébalo desde otra app (por ejemplo Safari, WhatsApp o tu correo) y vuelve a intentarlo.',
    };
  }

  if (!local.exists) {
    // The picker resolved but the sandbox copy never showed up -- the exact
    // failure mode reproduced with files coming from Chrome's iOS file
    // provider; picking the same file from WhatsApp instead worked fine, so
    // this is named as a provider problem rather than a Fino/Expo one.
    devLog(LOG_TAG, 'resolve_failed', {
      reason: 'copy_missing',
      originalUri: rawAsset.uri,
      localUri: local.uri,
      name: rawAsset.name,
    });
    return {
      ok: false,
      errorMessage:
        'No pudimos copiar ese archivo a Fino. Esto pasa a veces con archivos que vienen de apps como Chrome; ábrelo con Safari, o compártelo por WhatsApp o correo y guárdalo en Archivos, y vuelve a intentarlo aquí.',
    };
  }

  if (local.size === 0) {
    devLog(LOG_TAG, 'resolve_failed', { reason: 'empty_file', localUri: local.uri, name: rawAsset.name });
    return {
      ok: false,
      errorMessage: 'Ese archivo está vacío. Descarga de nuevo el estado de cuenta e inténtalo otra vez.',
    };
  }

  const asset: PickedAsset = { uri: local.uri, name: rawAsset.name, mimeType: rawAsset.mimeType ?? 'text/csv', size: local.size };
  devLog(LOG_TAG, 'resolve_success', asset);
  return { ok: true, asset };
}

/**
 * Archivo → Validación → Preview → Importación → Resultado.
 * Nothing is written until the user confirms what they are looking at.
 */
export default function ImportScreen() {
  const router = useRouter();
  // sharedUri/sharedName come from app/compartir.tsx (the fino:///compartir
  // share-sheet hand-off) -- present only when this screen was reached by
  // sharing a file into Fino instead of tapping "Elegir archivo" below.
  const { accountId, sharedUri, sharedName, onboarding, bankId } = useLocalSearchParams<{
    accountId: string;
    sharedUri?: string;
    sharedName?: string;
    onboarding?: string;
    bankId?: string;
  }>();
  // Onboarding funcional: esta pantalla es la MISMA para el onboarding inicial
  // y para "Cuentas → Agregar cuenta" -- `onboarding=1` solo cambia copy y el
  // destino final, nunca la lógica de subida/preview/confirmación de abajo.
  const isOnboarding = onboarding === '1';
  const bankConfig = bankId ? getBankImportConfig(bankId) : null;
  const setOnboarding = useAuthStore((state) => state.setOnboarding);
  const skipOnboarding = useSkipOnboarding();
  const [skipSheetVisible, setSkipSheetVisible] = useState(false);
  const refreshAfterImport = useRefreshAfterImport();
  const cancelImport = useCancelImport();
  // Detección de "banco distinto": el resolver del backend ya prueba TODOS
  // los parsers si el sugerido no reconoce el archivo (StatementParserResolver),
  // así que un archivo de otro banco ya se importa bien -- lo único que falta
  // es avisar, en vez de dejar que "· PICHINCHA" junto a la cuenta de
  // Guayaquil se lea como un error.
  const { data: account } = useAccount(accountId);

  // Arriving with a shared file already known (sharedUri/sharedName) skips
  // the "Elegir archivo" screen entirely -- starting on 'working' avoids a
  // one-frame flash of that button before the effect below takes over.
  const [step, setStep] = useState<Step>(sharedUri && sharedName ? 'working' : 'pick');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  // §8: se activa sola cuando hay un importId, así que no añade una espera al flujo.
  const withdrawalScan = useWithdrawalScan(result?.importId);
  const [error, setError] = useState<string | null>(null);
  const [excluded, setExcluded] = useState<Set<string>>(new Set());

  // Entregable 10 (Generic CSV mapper): the same picked file is re-sent to
  // /imports/manual once the person assigns columns by hand, so it has to
  // stay around after the first (failed) upload -- DocumentPicker's asset,
  // not the file's bytes, since `upload()` reads it lazily from its uri.
  const [pickedAsset, setPickedAsset] = useState<PickedAsset | null>(null);
  const [firstRowIsHeader, setFirstRowIsHeader] = useState(true);
  const [saveMapping, setSaveMapping] = useState(true);
  const [roleColumns, setRoleColumns] = useState<RoleColumns>(EMPTY_ROLE_COLUMNS);

  // Entregable 8: "no imports huérfanos por salir de la pantalla" -- a preview
  // is a real row in the backend (Received/PreviewReady) the moment upload()
  // resolves. Leaving this screen any other way than confirming -- the close
  // button, the ghost "Cancelar", the hardware back button, an edge swipe --
  // all unmount this component the same way, so a single cleanup here covers
  // every exit path instead of wiring each control by hand.
  const previewRef = useRef<ImportPreview | null>(null);
  const confirmedRef = useRef(false);
  const cancelImportRef = useRef(cancelImport.mutate);

  useEffect(() => {
    previewRef.current = preview;
  }, [preview]);

  cancelImportRef.current = cancelImport.mutate;

  useEffect(() => {
    // Empty deps: this must register exactly once and fire only on unmount --
    // not on every render, which is the only way the ref reads above stay
    // current without re-running the cleanup early.
    return () => {
      const pending = previewRef.current;
      if (pending && pending.status === 'PreviewReady' && !confirmedRef.current) {
        cancelImportRef.current(pending.importId);
      }
    };
  }, []);

  /**
   * Shared by the manual "Elegir archivo" flow and the share-sheet hand-off
   * below -- both end up with a validated PickedAsset and differ only in
   * how they got one.
   */
  const uploadAsset = async (asset: PickedAsset) => {
    if (!accountId) {
      return;
    }

    setPickedAsset(asset);
    setFirstRowIsHeader(true);
    setSaveMapping(true);
    setRoleColumns(EMPTY_ROLE_COLUMNS);
    setStep('working');

    // Antes esto solo se medía dentro del onboarding, así que una
    // importación hecha desde Cuentas no existía para analytics y
    // "% de usuarios que importan" salía sistemáticamente bajo. Ahora se
    // mide siempre y es `source` quien distingue el contexto.
    const analyticsSource = isOnboarding ? AnalyticsSource.Onboarding : AnalyticsSource.Accounts;
    const fileFormat = toFileFormat(asset.name);
    importStartedAtRef.current = Date.now();

    track(AnalyticsEvent.ImportStarted, { source: analyticsSource, fileFormat });

    try {
      devLog(LOG_TAG, 'upload_start', { name: asset.name, mimeType: asset.mimeType, size: asset.size });
      const response = await api.imports.upload(accountId, asset);

      devLog(LOG_TAG, 'upload_done', { status: response.status });
      setPreview(response);
      setStep('preview');

      if (response.status === 'Failed') {
        devLog(LOG_TAG, 'backend_reported_failed', { failureReason: response.failureReason });
        setError(response.failureReason ?? 'No pudimos leer el archivo.');
        track(AnalyticsEvent.ImportFailed, {
          source: analyticsSource,
          fileFormat,
          reason: ErrorReason.ParserFailed,
          durationBucket: elapsedImportBucket(),
        });
      }
    } catch (uploadError) {
      devLog(LOG_TAG, 'upload_failed', {
        isApiError: uploadError instanceof ApiError,
        status: uploadError instanceof ApiError ? uploadError.status : null,
        ...serializeError(uploadError),
      });
      setError(
        uploadError instanceof ApiError
          ? uploadError.message
          : 'No pudimos subir el archivo. Si el problema sigue, prueba eligiéndolo de nuevo desde otra app.',
      );
      track(AnalyticsEvent.ImportFailed, {
        source: analyticsSource,
        fileFormat,
        reason:
          uploadError instanceof ApiError
            ? uploadError.status >= 500
              ? ErrorReason.ServerError
              : ErrorReason.UploadError
            : ErrorReason.NetworkError,
        durationBucket: elapsedImportBucket(),
      });
      setStep('pick');
    }
  };

  // Entregable "Compartir hacia Fino": a shared file arrives as route params
  // instead of a DocumentPicker result, so this runs once on mount (guarded
  // by the ref, not by `step`, since a failed resolve leaves step at 'pick'
  // and must not retry itself on the next render) and pushes it through the
  // exact same resolvePickedFile() gate a manual pick goes through -- same
  // extension whitelist, same "did the copy actually land" check.
  const sharedHandledRef = useRef(false);

  /**
   * §18: cuánto tardó la importación, en tramos. El milisegundo exacto no
   * responde ninguna pregunta que el tramo no responda, y combinado con la
   * hora del evento sería casi un identificador.
   */
  const importStartedAtRef = useRef<number | null>(null);
  const elapsedImportBucket = () =>
    importStartedAtRef.current === null
      ? undefined
      : toDurationBucket(Date.now() - importStartedAtRef.current);

  useEffect(() => {
    if (sharedHandledRef.current || !sharedUri || !sharedName || !accountId) {
      return;
    }
    sharedHandledRef.current = true;

    devLog(LOG_TAG, 'shared_file_received', { uri: sharedUri, name: sharedName });

    const rawAsset: DocumentPicker.DocumentPickerAsset = {
      uri: sharedUri,
      name: sharedName,
      mimeType: guessMimeType(sharedName),
      lastModified: Date.now(),
    };

    const resolved = resolvePickedFile(rawAsset);
    if (!resolved.ok) {
      setError(resolved.errorMessage);
      return;
    }

    void uploadAsset(resolved.asset);
    // uploadAsset intentionally omitted: it closes over accountId (already
    // in the guard above) and every setter it calls is stable -- adding it
    // would re-run this effect on every render instead of once per share.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sharedUri, sharedName, accountId]);

  const pickAndUpload = async () => {
    setError(null);

    // The root cause of the original "No pudimos abrir el archivo
    // seleccionado. Descárgalo en este iPhone y vuelve a intentarlo." report
    // (a string that exists nowhere in Fino's frontend or backend) was
    // narrowed down on Anderson's own phone: files coming from Chrome's iOS
    // file provider failed every time, while the identical file picked from
    // WhatsApp instead opened fine -- ruling out Expo Go, iOS's document
    // picker itself, and this component. copyToCacheDirectory: true is the
    // correct setting regardless (upload() needs a plain local file it can
    // read without security-scoped-resource handling); resolvePickedFile()
    // below is what actually checks whether that copy produced a real file,
    // so a broken provider gets a specific, actionable message instead of a
    // generic upload failure three steps later.
    track(AnalyticsEvent.FilePickerOpened, {
      source: isOnboarding ? AnalyticsSource.Onboarding : AnalyticsSource.Accounts,
    });
    devLog(LOG_TAG, 'picker_open', { acceptedTypes: ACCEPTED_TYPES, platform: Platform.OS });

    let picked: DocumentPicker.DocumentPickerResult;
    try {
      picked = await DocumentPicker.getDocumentAsync({
        type: ACCEPTED_TYPES,
        copyToCacheDirectory: true,
        multiple: false,
      });
    } catch (pickerError) {
      devLog(LOG_TAG, 'picker_threw', serializeError(pickerError));
      setError('No pudimos abrir el selector de archivos. Cierra la app por completo y vuelve a intentarlo.');
      return;
    }

    if (picked.canceled || !picked.assets?.[0]) {
      devLog(LOG_TAG, 'picker_canceled', {});
      return;
    }

    const rawAsset = picked.assets[0];
    devLog(LOG_TAG, 'picker_success', {
      uri: rawAsset.uri,
      name: rawAsset.name,
      mimeType: rawAsset.mimeType,
      size: rawAsset.size,
      hasFile: Boolean(rawAsset.file),
    });

    if (!accountId) {
      return;
    }

    const resolved = resolvePickedFile(rawAsset);
    if (!resolved.ok) {
      setError(resolved.errorMessage);
      return;
    }

    await uploadAsset(resolved.asset);
  };

  const submitManualMapping = async () => {
    if (!pickedAsset || !accountId || roleColumns.date === null || roleColumns.description === null) {
      return;
    }

    // pickedAsset's uri points at the cache copy made back in pickAndUpload,
    // possibly minutes ago (the person had to read the mapping screen and
    // tap through columns in between). That copy is a plain sandbox file,
    // not a security-scoped handle, so it does not expire the way the
    // picker's original uri could -- but iOS is allowed to purge the whole
    // Caches directory under storage pressure while the app is backgrounded,
    // so it is worth confirming the file is still actually there rather than
    // sending an upload that can only fail with a confusing network error.
    // Skipped on web: there is no sandbox-copy/Caches-purge model there, and
    // expo-file-system's File class has no real implementation on that
    // platform to check against.
    if (Platform.OS !== 'web') {
      const stillThere = new File(pickedAsset.uri);
      devLog(LOG_TAG, 'remapping_recheck', { uri: pickedAsset.uri, exists: stillThere.exists });
      if (!stillThere.exists || stillThere.size === 0) {
        setError('El archivo que elegiste ya no está disponible. Vuelve a elegirlo e inténtalo de nuevo.');
        setPickedAsset(null);
        setStep('pick');
        return;
      }
    }

    const mapping: ManualColumnMapping = {
      firstRowIsHeader,
      dateColumn: roleColumns.date!,
      descriptionColumn: roleColumns.description!,
      amountColumn: roleColumns.amount,
      debitColumn: roleColumns.debit,
      creditColumn: roleColumns.credit,
      referenceColumn: roleColumns.reference,
      saveMapping,
    };

    setStep('working');
    setError(null);

    try {
      devLog(LOG_TAG, 'remapping_upload_start', { name: pickedAsset.name });
      const response = await api.imports.uploadManual(accountId, pickedAsset, mapping);
      devLog(LOG_TAG, 'remapping_upload_done', { status: response.status });
      setPreview(response);
      setStep('preview');

      if (response.status === 'Failed') {
        setError(response.failureReason ?? 'No pudimos leer el archivo con ese mapeo.');
      }
    } catch (mappingError) {
      devLog(LOG_TAG, 'remapping_upload_failed', {
        isApiError: mappingError instanceof ApiError,
        status: mappingError instanceof ApiError ? mappingError.status : null,
        ...serializeError(mappingError),
      });
      setError(mappingError instanceof ApiError ? mappingError.message : 'No pudimos procesar el mapeo.');
      setStep('mapping');
    }
  };

  const setRole = (role: ColumnRole, columnIndex: number) => {
    setRoleColumns((current) => {
      const next = { ...current };
      // A column keeps at most one role, and a role belongs to at most one
      // column -- tapping the same chip again clears it, tapping a different
      // one moves the role there instead of duplicating it.
      for (const key of ROLE_ORDER) {
        if (next[key] === columnIndex && key !== role) {
          next[key] = null;
        }
      }
      next[role] = next[role] === columnIndex ? null : columnIndex;
      return next;
    });
  };

  const mappingIsValid = roleColumns.date !== null
    && roleColumns.description !== null
    && (roleColumns.amount !== null || roleColumns.debit !== null || roleColumns.credit !== null);

  const confirm = async () => {
    if (!preview) {
      return;
    }

    setStep('working');
    try {
      // Entregable: the statement itself declares its own closing balance (the
      // bank's own number, right there in the file) -- applying it is what
      // turns "estimado" into "verificado" and is what makes the balance match
      // reality instead of just netting this batch's flows from zero. Was
      // hardcoded false here, so a fresh account's balance never reflected an
      // import even when it included income: RecalculateBalanceAsync had no
      // verified anchor to add that income on top of.
      const confirmation = await api.imports.confirm(preview.importId, Array.from(excluded), true);
      confirmedRef.current = true;
      setResult(confirmation);
      refreshAfterImport();
      setStep('done');

      // "import_completed" es la métrica principal del spec (tasa de primera
      // importación exitosa). El conteo viaja BUCKETIZADO: el número exacto de
      // movimientos de un estado de cuenta es un dato de esa persona, y el
      // tramo responde igual de bien "¿importan archivos grandes o pequeños?".
      track(AnalyticsEvent.ImportCompleted, {
        source: isOnboarding ? AnalyticsSource.Onboarding : AnalyticsSource.Accounts,
        sourceType: 'bank_statement',
        fileFormat: toFileFormat(pickedAsset?.name),
        transactionCountBucket: toCountBucket(confirmation.importedCount),
        durationBucket: elapsedImportBucket(),
      });

      void trackOnce(AnalyticsEvent.FirstImportCompleted, {
        fileFormat: toFileFormat(pickedAsset?.name),
      });

      if (isOnboarding) {
        track(AnalyticsEvent.OnboardingCompleted);
        // El backend ya marcó "primera importación completada" al confirmar
        // (ImportService.ConfirmAsync) -- esto solo refresca la sesión en
        // memoria para que un cierre de la app justo aquí no vuelva a mostrar
        // el onboarding. Sin bloquear: si falla, se reconcilia en el próximo
        // login.
        void api.onboarding.get().then(setOnboarding).catch(() => undefined);
      }
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

  const confirmSkip = () => {
    skipOnboarding.mutate(undefined, {
      onSuccess: () => router.replace('/(tabs)'),
    });
  };

  return (
    <Screen>
      {isOnboarding && step !== 'done' ? (
        <OnboardingTopBar
          stage="importa"
          onBack={() => router.back()}
          onSkip={() => setSkipSheetVisible(true)}
        />
      ) : isOnboarding ? null : (
        <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
          <Ionicons name="close" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Cerrar
          </Typo>
        </Pressable>
      )}

      {step === 'pick' ? (
        <View style={styles.block}>
          <Typo variant="title">{isOnboarding ? 'Elige el archivo' : 'Importar movimientos'}</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {isOnboarding
              ? `Busca el archivo que descargaste de ${bankConfig?.displayName ?? 'tu banco'}. Te mostramos todo antes de guardar nada.`
              : 'Sube el estado de cuenta que descargaste de tu banco. Aceptamos CSV y XLSX. Te mostramos todo antes de guardar nada.'}
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
            {isOnboarding ? 'Leyendo tu archivo…' : 'Procesando tu archivo…'}
          </Typo>
        </View>
      ) : null}

      {step === 'preview' && preview ? (
        <View style={styles.block}>
          {isOnboarding ? (
            <ImportProcessingReveal preview={preview} bankTitle={bankConfig?.displayName ?? 'tu banco'} />
          ) : (
            <>
              <Typo variant="title">
                {preview.status === 'Failed' ? 'No pudimos leerlo' : `Encontramos ${preview.totalRows} movimientos`}
              </Typo>
              <Typo variant="caption" color={colors.textSecondary}>
                {preview.fileName}
                {preview.parserCode ? `  ·  ${preview.parserCode}` : ''}
              </Typo>
            </>
          )}

          {preview.status === 'Failed' ? (
            <>
              <View style={styles.errorBox}>
                <Ionicons name="alert-circle-outline" size={17} color={colors.danger} />
                <Typo variant="caption" color={colors.danger} style={styles.flex}>
                  {preview.failureReason ?? error}
                </Typo>
              </View>
              {preview.unmappedColumns && preview.unmappedColumns.length > 0 ? (
                <Button label="Elegir columnas a mano" variant="secondary" onPress={() => setStep('mapping')} />
              ) : null}
              <Button label="Probar con otro archivo" variant="ghost" onPress={() => setStep('pick')} />
            </>
          ) : (
            <>
              {account && preview.detectedProviderCode && preview.detectedProviderCode !== account.providerCode ? (
                <View style={styles.noticeBox}>
                  <Ionicons name="swap-horizontal-outline" size={17} color={colors.warning} />
                  <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
                    Este archivo parece ser de{' '}
                    {getBankImportConfig(preview.detectedProviderCode)?.displayName ?? preview.detectedProviderCode},
                    no de {account.providerName}. Lo importamos igual, usando el formato correcto.
                  </Typo>
                </View>
              ) : null}

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

      {step === 'mapping' && preview?.unmappedColumns ? (
        <View style={styles.block}>
          <Typo variant="title">¿Qué es cada columna?</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            No reconocimos el formato de {preview.fileName}. Dinos qué contiene cada columna
            y usaremos exactamente las mismas reglas de duplicados y categorías que con
            un banco que sí reconocemos.
          </Typo>

          <Pressable style={styles.toggleRow} onPress={() => setFirstRowIsHeader((current) => !current)}>
            <Ionicons
              name={firstRowIsHeader ? 'checkbox' : 'square-outline'}
              size={20}
              color={firstRowIsHeader ? colors.accent : colors.textSecondary}
            />
            <Typo variant="body" style={styles.flex}>
              La primera fila es un encabezado, no un movimiento
            </Typo>
          </Pressable>

          <ScrollView style={styles.mappingScroll} nestedScrollEnabled>
            {preview.unmappedColumns.map((headerText, columnIndex) => {
              const sample = preview.unmappedSampleRows?.[0]?.[columnIndex];
              return (
                <View key={columnIndex} style={styles.mappingColumn}>
                  <View style={styles.flex}>
                    <Typo variant="bodyStrong" numberOfLines={1}>
                      {headerText || `Columna ${columnIndex + 1}`}
                    </Typo>
                    {sample ? (
                      <Typo variant="caption" color={colors.textSecondary} numberOfLines={1}>
                        ej. {sample}
                      </Typo>
                    ) : null}
                  </View>
                  <View style={styles.chipRow}>
                    {ROLE_ORDER.map((role) => {
                      const selected = roleColumns[role] === columnIndex;
                      return (
                        <Pressable
                          key={role}
                          onPress={() => setRole(role, columnIndex)}
                          style={[styles.chip, selected ? styles.chipSelected : null]}
                        >
                          <Typo variant="caption" color={selected ? colors.onAccent : colors.textSecondary}>
                            {ROLE_LABELS[role]}
                          </Typo>
                        </Pressable>
                      );
                    })}
                  </View>
                </View>
              );
            })}
          </ScrollView>

          <Pressable style={styles.toggleRow} onPress={() => setSaveMapping((current) => !current)}>
            <Ionicons
              name={saveMapping ? 'checkbox' : 'square-outline'}
              size={20}
              color={saveMapping ? colors.accent : colors.textSecondary}
            />
            <Typo variant="body" style={styles.flex}>
              Recordar este mapeo para los próximos archivos de esta cuenta
            </Typo>
          </Pressable>

          {!mappingIsValid ? (
            <Typo variant="caption" color={colors.textSecondary}>
              Falta asignar Fecha, Descripción y Monto (o Débito/Crédito).
            </Typo>
          ) : null}

          <Button label="Continuar" onPress={() => void submitManualMapping()} disabled={!mappingIsValid} />
          <Button label="Cancelar" variant="ghost" onPress={() => setStep('preview')} />
        </View>
      ) : null}

      {step === 'done' && result ? (
        <View style={styles.block}>
          <View style={styles.doneIcon}>
            <Ionicons name="checkmark" size={26} color={colors.onAccent} />
          </View>

          <Typo variant="title">{isOnboarding ? 'Fino ya tiene tus movimientos' : 'Listo'}</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Importamos {result.importedCount} movimiento(s).
            {result.upgradedCount > 0
              ? ` Actualizamos ${result.upgradedCount} que ya tenías detectado(s) por correo con el dato exacto del banco.`
              : ''}
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

          {/* §8: "Encontramos 3 posibles retiros [Revisar]". Se consulta DESPUÉS de
              importar y no bloquea nada: la importación ya terminó y fue un éxito,
              esto es solo una invitación a revisar cuando quiera. */}
          {withdrawalScan.data && withdrawalScan.data.candidateCount > 0 ? (
            <Card tone="secondary">
              <View style={styles.withdrawalNotice}>
                <Ionicons name="cash-outline" size={18} color={colors.text} />
                <Typo variant="body" style={styles.withdrawalNoticeText}>
                  {withdrawalScan.data.candidateCount === 1
                    ? 'Encontramos 1 posible retiro en efectivo.'
                    : `Encontramos ${withdrawalScan.data.candidateCount} posibles retiros en efectivo.`}
                </Typo>
              </View>
              <Typo variant="caption" color={colors.textSecondary}>
                Si ese dinero pasó a tu efectivo, no debería contar como gasto.
              </Typo>
              <Button
                label="Revisar retiros"
                variant="secondary"
                compact
                fullWidth={false}
                onPress={() => router.push('/transferencias')}
              />
            </Card>
          ) : null}

          <Button
            label={isOnboarding ? 'Ver mi dinero' : 'Ver mis movimientos'}
            onPress={() => router.replace(isOnboarding ? '/(tabs)' : '/(tabs)/movimientos')}
          />
        </View>
      ) : null}

      <SkipOnboardingSheet
        visible={skipSheetVisible}
        loading={skipOnboarding.isPending}
        onDismiss={() => setSkipSheetVisible(false)}
        onConfirmSkip={confirmSkip}
      />
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
  withdrawalNotice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  withdrawalNoticeText: {
    flex: 1,
  },
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
  toggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  mappingScroll: {
    maxHeight: 420,
  },
  mappingColumn: {
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    marginBottom: spacing.md,
  },
  chipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  chip: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
    backgroundColor: colors.surfaceSecondary,
  },
  chipSelected: {
    backgroundColor: colors.accent,
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
