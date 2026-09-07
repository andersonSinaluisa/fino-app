import { ExtensionStorage } from '@bacons/apple-targets';
import { File } from 'expo-file-system';
import { WIDGET_APP_GROUP } from './widgets/constants';
import type { PendingSharedFile } from './shareImport.types';

export type { PendingSharedFile } from './shareImport.types';

/** Must match ShareViewController.swift's `pendingShareFileKey` byte-for-byte. */
const PENDING_SHARE_FILE_KEY = 'pendingShareFilePath';

const storage = new ExtensionStorage(WIDGET_APP_GROUP);

/**
 * targets/share/ShareViewController.swift copies the shared file into the
 * App Group container and writes its file:// URI here before opening
 * fino:///compartir -- by the time app/compartir.tsx mounts, that write has
 * already happened (the deep link itself is the "it's ready" signal), so
 * this is a plain synchronous UserDefaults read, not a race. The main app
 * shares the same App Group entitlement as the extension, so the returned
 * file:// path is a real local file this process can read directly, no
 * differently from a file `expo-document-picker` copied into the cache.
 *
 * Takes the same optional filename parameter the Android implementation
 * requires, purely so both platforms share one call signature -- iOS never
 * needs it, since ExtensionStorage already names the exact file.
 */
export async function readPendingSharedFile(_fileNameFromDeepLink?: string): Promise<PendingSharedFile | null> {
  const uri = storage.get(PENDING_SHARE_FILE_KEY);
  if (!uri) {
    return null;
  }

  const file = new File(uri);
  if (!file.exists) {
    return null;
  }

  return { uri: file.uri, name: file.name };
}

export function clearPendingSharedFile(): void {
  storage.remove(PENDING_SHARE_FILE_KEY);
}
