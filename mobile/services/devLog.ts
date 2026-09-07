/**
 * Tiny __DEV__-only logging helper. Shared by the import screen and the
 * low-level `upload()` client so the same pick -> resolve -> upload pipeline
 * can be traced across both files with one consistent shape.
 *
 * Never pass file contents, parsed rows, or transaction data here -- only
 * picker/filesystem/network metadata (uri, name, mimeType, size, stage,
 * error name/message/stack). That is enough to tell "the OS never gave us a
 * real file" apart from "we have a file but the network/backend rejected
 * it", without ever risking a bank statement's contents in a log line.
 */
export function devLog(tag: string, stage: string, details: Record<string, unknown> = {}): void {
  if (__DEV__) {
    // eslint-disable-next-line no-console
    console.log(`[${tag}] ${stage}`, details);
  }
}

/** Consistent shape for logging any caught value, Error or not. */
export function serializeError(error: unknown): { name?: string; message: string; stack?: string } {
  return {
    name: error instanceof Error ? error.name : undefined,
    message: error instanceof Error ? error.message : String(error),
    stack: error instanceof Error ? error.stack : undefined,
  };
}

/** `file:` / `content:` / `blob:` / `data:` / ... -- never the full uri. */
export function uriSchemeOf(uri: string): string {
  return /^([a-zA-Z][a-zA-Z0-9+.-]*):/.exec(uri)?.[1] ?? 'unknown';
}
