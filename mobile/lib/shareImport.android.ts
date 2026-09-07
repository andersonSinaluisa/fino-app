import { File, Paths } from 'expo-file-system';
import type { PendingSharedFile } from './shareImport.types';

export type { PendingSharedFile } from './shareImport.types';

/** Must match plugins/withShareIntent.js's `File(filesDir, "share-inbox")` subfolder name. */
const SHARE_INBOX_DIR = 'share-inbox';

/**
 * MainActivity's rewriteShareIntent() (injected by plugins/withShareIntent.js)
 * copies the shared file into filesDir/share-inbox/<name> before rewriting
 * the intent into the fino:///compartir?file=<name> deep link -- exactly
 * Paths.document/share-inbox/<name> from this side, since expo-file-system's
 * own document directory resolves to the same `context.filesDir` on Android
 * (confirmed in expo-modules-core's AppDirectoriesService.persistentFilesDirectory).
 * Unlike iOS there's no App-Group/UserDefaults boundary to cross and nothing
 * persisted beyond the file itself -- the deep link's `file` query param is
 * the only handoff needed, so it's required here rather than read from
 * shared storage.
 */
export async function readPendingSharedFile(fileNameFromDeepLink?: string): Promise<PendingSharedFile | null> {
  if (!fileNameFromDeepLink) {
    return null;
  }

  const file = new File(Paths.document, SHARE_INBOX_DIR, fileNameFromDeepLink);
  if (!file.exists) {
    return null;
  }

  return { uri: file.uri, name: file.name };
}

export function clearPendingSharedFile(): void {
  // No-op: no persisted "pending" marker on Android beyond the file itself
  // (already located via the deep link's own `file` param above) -- there
  // is nothing else to clear.
}
