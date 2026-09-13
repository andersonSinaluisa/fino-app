import { useCallback, useRef, useState } from 'react';
import * as Haptics from 'expo-haptics';
import { useAccounts, useCategories, useQuickEntryBootstrap, useTransactions } from './queries';
import { createReceiptExtractor } from '../lib/receipts/extractor';
import { prepareReceiptImage, looksTooSmall } from '../lib/receipts/prepareImage';
import { PickOutcome, chooseReceiptPhoto, takeReceiptPhoto, type PickedImage } from '../lib/receipts/pickImage';
import { findPossibleDuplicates, findReceiptMatches, type MatchOutcome } from '../lib/receipts/matchTransaction';
import { receiptToDraft, type ReceiptDraft } from '../lib/receipts/toDraft';
import {
  ReceiptSource,
  ReceiptWarning,
  emptyExtraction,
  type ReceiptExtractionResult,
  type ReceiptSourceValue,
} from '../lib/receipts/types';
import {
  AnalysisResult,
  AnalyticsEvent,
  ErrorReason,
  toDurationBucket,
  track,
  trackOnce,
  type AnalysisResultValue,
} from '../services/analytics';
import type { TransactionMatch } from '../lib/receipts/matchTransaction';

export type ReceiptScanStage = 'idle' | 'analyzing' | 'review' | 'failed';

export interface ReceiptScanState {
  stage: ReceiptScanStage;
  imageUri: string | null;
  source: ReceiptSourceValue | null;
  extraction: ReceiptExtractionResult | null;
  draft: ReceiptDraft | null;
  /** §22: el movimiento del banco que parece ser este mismo gasto. */
  bankMatch: MatchOutcome | null;
  /** §26: gastos que la persona ya registró a mano y podrían ser este. */
  duplicates: TransactionMatch[];
  /** §6: la foto parece demasiado pequeña para leerla bien. No bloquea. */
  lowQuality: boolean;
  /** Motivo sanitizado del fallo, para el copy. */
  failure: string | null;
  /** True si el build no trae el módulo de OCR o el de cámara. */
  unavailable: boolean;
}

const INITIAL: ReceiptScanState = {
  stage: 'idle',
  imageUri: null,
  source: null,
  extraction: null,
  draft: null,
  bankMatch: null,
  duplicates: [],
  lowQuality: false,
  failure: null,
  unavailable: false,
};

/**
 * §9: el pipeline completo del escaneo, en un solo sitio.
 *
 *   imagen -> preparación -> OCR -> parser -> match/duplicados -> borrador
 *
 * La pantalla no conoce al extractor ni al parser: pide "escanea" y recibe
 * estado. Eso es lo que permite cambiar el motor (§14) sin tocar la UI.
 *
 * No crea el movimiento: devuelve un `ReceiptDraft` que la pantalla de
 * revisión manda por el MISMO camino que el registro rápido (§36).
 */
export function useReceiptScan() {
  const [state, setState] = useState<ReceiptScanState>(INITIAL);
  const startedAt = useRef<number | null>(null);

  const { data: categories } = useCategories();
  const { data: bootstrap } = useQuickEntryBootstrap();
  const { data: accounts } = useAccounts();
  // La ventana de conciliación es de días: la primera página de movimientos
  // recientes basta y ya está en caché por la pestaña Movimientos.
  const { data: transactions } = useTransactions({});

  const reset = useCallback(() => {
    setState(INITIAL);
    startedAt.current = null;
  }, []);

  const analyze = useCallback(
    async (picked: PickedImage) => {
      if (picked.outcome !== PickOutcome.Picked || !picked.uri) {
        return;
      }

      startedAt.current = Date.now();
      track(AnalyticsEvent.ReceiptCaptureCompleted, { source: picked.source });
      track(AnalyticsEvent.ReceiptAnalysisStarted, { source: picked.source });

      setState({
        ...INITIAL,
        stage: 'analyzing',
        imageUri: picked.uri,
        source: picked.source,
        lowQuality: looksTooSmall(picked.width, picked.height),
      });

      const extractor = createReceiptExtractor();

      if (!extractor) {
        track(AnalyticsEvent.ReceiptAnalysisFailed, {
          source: picked.source,
          reason: ErrorReason.Unknown,
        });

        setState((current) => ({ ...current, stage: 'failed', unavailable: true, failure: ErrorReason.Unknown }));
        return;
      }

      // §41-42: orientación, tamaño y --sobre todo-- fuera el EXIF antes de
      // que la imagen toque nada más.
      const prepared = await prepareReceiptImage(picked.uri, picked.width ?? undefined);
      const extraction = await extractor.extract(prepared.uri);

      const elapsed = startedAt.current === null ? null : Date.now() - startedAt.current;
      const durationBucket = elapsed === null ? undefined : toDurationBucket(elapsed);

      if (extraction.total === null) {
        // §30: sin total no se puede crear un movimiento, pero no es un error
        // de la app: se ofrece repetir la foto o registrar a mano.
        track(AnalyticsEvent.ReceiptAnalysisFailed, {
          source: picked.source,
          reason: extraction.warnings.includes(ReceiptWarning.PossiblyCropped)
            ? ErrorReason.NotRecognized
            : ErrorReason.ParserFailed,
          durationBucket,
        });

        setState((current) => ({
          ...current,
          stage: 'failed',
          extraction,
          failure: extraction.warnings.includes(ReceiptWarning.PossiblyCropped)
            ? ReceiptWarning.PossiblyCropped
            : ReceiptWarning.NoTotal,
        }));
        return;
      }

      const analysisResult = resultQuality(extraction);

      track(AnalyticsEvent.ReceiptAnalysisCompleted, {
        source: picked.source,
        analysisResult,
        totalDetected: true,
        dateDetected: extraction.date !== null,
        merchantDetected: extraction.merchantName !== null,
        paymentHintDetected: extraction.paymentHint !== 'unknown',
        durationBucket,
      });

      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => undefined);

      const cashAccountId = resolveCashAccountId(bootstrap?.cashAccountId, accounts);
      const draft = receiptToDraft(extraction, { categories, cashAccountId });

      const rows = transactions?.pages.flatMap((page) => page.items) ?? [];
      const bankMatch = findReceiptMatches(extraction, rows);
      const duplicates = findPossibleDuplicates(extraction, rows);

      if (bankMatch.candidates.length > 0) {
        track(AnalyticsEvent.ReceiptMatchedExistingTransaction, {
          confidence: bankMatch.best?.confidence ?? 'ambiguous',
          ambiguous: bankMatch.ambiguous,
        });
      }

      if (duplicates.length > 0) {
        track(AnalyticsEvent.ReceiptPossibleDuplicateShown);
      }

      track(AnalyticsEvent.ReceiptReviewOpened, { source: picked.source, analysisResult });
      void trackOnce(AnalyticsEvent.FirstReceiptScanned, { source: picked.source });

      setState((current) => ({
        ...current,
        stage: 'review',
        imageUri: prepared.uri,
        extraction,
        draft,
        bankMatch,
        duplicates,
      }));
    },
    [accounts, bootstrap?.cashAccountId, categories, transactions],
  );

  const fromCamera = useCallback(async () => {
    track(AnalyticsEvent.ReceiptCameraOpened);
    const picked = await takeReceiptPhoto();
    await handlePicked(picked, setState, analyze);
  }, [analyze]);

  const fromGallery = useCallback(async () => {
    track(AnalyticsEvent.ReceiptGalleryOpened);
    const picked = await chooseReceiptPhoto();
    await handlePicked(picked, setState, analyze);
  }, [analyze]);

  const retry = useCallback(
    async (source: ReceiptSourceValue) => {
      track(AnalyticsEvent.ReceiptRetry, { source, reason: ErrorReason.NotRecognized });
      reset();

      if (source === ReceiptSource.Camera) {
        await fromCamera();
        return;
      }

      await fromGallery();
    },
    [fromCamera, fromGallery, reset],
  );

  /** Para que la pantalla pueda editar el borrador sin recalcular nada. */
  const updateDraft = useCallback((patch: Partial<ReceiptDraft>) => {
    setState((current) => (current.draft ? { ...current, draft: { ...current.draft, ...patch } } : current));
  }, []);

  return { state, fromCamera, fromGallery, retry, reset, updateDraft };
}

async function handlePicked(
  picked: PickedImage,
  setState: (updater: (current: ReceiptScanState) => ReceiptScanState) => void,
  analyze: (picked: PickedImage) => Promise<void>,
): Promise<void> {
  if (picked.outcome === PickOutcome.Picked) {
    await analyze(picked);
    return;
  }

  if (picked.outcome === PickOutcome.Cancelled) {
    return;
  }

  setState((current) => ({
    ...current,
    stage: 'failed',
    source: picked.source,
    unavailable: picked.outcome === PickOutcome.Unavailable,
    failure:
      picked.outcome === PickOutcome.PermissionDenied ? ErrorReason.PermissionDenied : ErrorReason.Unknown,
  }));
}

/**
 * §37: `complete` cuando salieron las tres cosas de prioridad alta, `partial`
 * cuando salió el total pero falta algo. Es una métrica de calidad del OCR
 * que no dice qué se leyó.
 */
function resultQuality(extraction: ReceiptExtractionResult): AnalysisResultValue {
  if (extraction.total === null) {
    return AnalysisResult.Failed;
  }

  return extraction.date !== null && extraction.merchantName !== null
    ? AnalysisResult.Complete
    : AnalysisResult.Partial;
}

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
const CASH_PROVIDER_CODE = 'EFECTIVO';

/**
 * El bootstrap devuelve el GUID vacío mientras la cuenta de efectivo todavía
 * no existe (se crea sola al guardar el primer movimiento en efectivo). En
 * ese caso no se preselecciona ninguna cuenta.
 */
function resolveCashAccountId(
  bootstrapId: string | undefined,
  accounts: readonly { id: string; providerCode: string; isArchived: boolean }[] | undefined,
): string | null {
  if (bootstrapId && bootstrapId !== EMPTY_GUID) {
    return bootstrapId;
  }

  const found = accounts?.find(
    (account) => account.providerCode === CASH_PROVIDER_CODE && !account.isArchived,
  );

  return found?.id ?? null;
}

export { emptyExtraction };
