import type { PendingSharedFile } from './shareImport.types';

export type { PendingSharedFile } from './shareImport.types';

/**
 * Reads the file a share-sheet hand-off left waiting for the app to pick
 * up (see targets/share/ShareViewController.swift on iOS and
 * plugins/withShareIntent.js's MainActivity patch on Android), so
 * app/compartir.tsx can hand its uri/name straight to
 * app/cuentas/importar.tsx -- which runs it through the exact same
 * resolvePickedFile() validation a manually-picked file already goes
 * through, so there is no separate accept/reject logic to keep in sync.
 *
 * Metro picks shareImport.ios.ts / shareImport.android.ts automatically for
 * those platforms (tsconfig.json's `moduleSuffixes`, the same trick
 * lib/widgets/sync.ts uses for its own per-platform split) -- this file is
 * the fallback for any other platform (this app ships no share-sheet
 * integration on web). All three variants keep this exact signature so
 * tsc's forced single-platform resolution under `moduleSuffixes` never
 * sees a mismatch.
 */
export async function readPendingSharedFile(_fileNameFromDeepLink?: string): Promise<PendingSharedFile | null> {
  return null;
}

export function clearPendingSharedFile(): void {
  // No-op: no share-sheet integration on this platform.
}
