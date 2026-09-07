/**
 * The importer's file-type whitelist and its extension helpers -- split out
 * from app/cuentas/importar.tsx so they're unit-testable and reusable (the
 * share-sheet hand-off in app/compartir.tsx needs the same MIME guess for a
 * file that never went through DocumentPicker) without pulling in
 * expo-router and the rest of that screen's dependency tree: importing a
 * route file directly from a test drags in expo-router's ESM-only
 * `standard-navigation` dependency, which this project's Jest config can't
 * transform.
 *
 * Some iOS file providers (Chrome's own among them -- confirmed on
 * Anderson's phone) don't respect the `type` filter passed to the picker
 * and hand back whatever `mimeType` they feel like, including a generic
 * 'application/octet-stream'. The extension is the one signal every
 * provider gets right, so it's the real gate for "is this a file we can
 * parse" -- mimeType only decides what we tell the backend it is.
 */
export const ACCEPTED_EXTENSIONS = ['csv', 'xls', 'xlsx', 'txt'];

export function extensionOf(fileName: string): string | null {
  const dot = fileName.lastIndexOf('.');
  return dot === -1 ? null : fileName.slice(dot + 1).toLowerCase();
}

const EXTENSION_MIME_TYPES: Record<string, string> = {
  csv: 'text/csv',
  xls: 'application/vnd.ms-excel',
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  txt: 'text/plain',
};

/**
 * A file that arrived via the share sheet never went through
 * DocumentPicker, so nothing ever reported its MIME type -- only its name
 * is known. resolvePickedFile() doesn't actually trust mimeType for
 * validation (the extension is the real gate, see ACCEPTED_EXTENSIONS
 * above), so a best-effort guess from the extension is all that's needed
 * here; it only ever reaches the backend as the multipart Content-Type.
 */
export function guessMimeType(fileName: string): string {
  const ext = extensionOf(fileName);
  return (ext && EXTENSION_MIME_TYPES[ext]) || 'application/octet-stream';
}
