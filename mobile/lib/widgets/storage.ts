import { File, Paths } from 'expo-file-system';

/**
 * Android-only persistence for the widget snapshot (see sync.android.ts).
 *
 * Unlike iOS -- where the widget extension is a separate native process that
 * reads ExtensionStorage/UserDefaults directly -- an Android App Widget's
 * "headless" JS task handler (react-native-android-widget) runs in a fresh
 * JS context with no access to the running app's React Query cache or
 * Zustand stores. So the app also persists the snapshot to a plain JSON
 * file the headless handler can read on a cold, OS-triggered update
 * (periodic refresh, device reboot, etc). `expo-file-system` is already a
 * dependency of this project (used elsewhere for imports), so this adds no
 * new native module.
 */

const SNAPSHOT_FILE_NAME = 'fino-widget-snapshot.json';

function snapshotFile(): File {
  return new File(Paths.document, SNAPSHOT_FILE_NAME);
}

export function writeSnapshotFile(json: string): void {
  snapshotFile().write(json);
}

export async function readSnapshotFile(): Promise<string | null> {
  const file = snapshotFile();
  if (!file.exists) {
    return null;
  }
  return file.text();
}

/**
 * Per-widget-instance picks for the two configurable widgets ("Presupuesto"
 * picks a category, "Cuenta" picks an account) -- keyed by Android's numeric
 * `widgetInfo.widgetId`, which is stable for the life of that one widget on
 * the home screen. Written by the configuration screen, read by the
 * headless task handler on every later OS-triggered update.
 */
const SELECTIONS_FILE_NAME = 'fino-widget-selections.json';

function selectionsFile(): File {
  return new File(Paths.document, SELECTIONS_FILE_NAME);
}

async function readSelections(): Promise<Record<string, string>> {
  const file = selectionsFile();
  if (!file.exists) {
    return {};
  }
  try {
    const parsed = JSON.parse(await file.text());
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : {};
  } catch {
    return {};
  }
}

export async function readSelection(widgetId: number): Promise<string | null> {
  const selections = await readSelections();
  return selections[String(widgetId)] ?? null;
}

export async function writeSelection(widgetId: number, selectedId: string): Promise<void> {
  const current = await readSelections();
  current[String(widgetId)] = selectedId;
  selectionsFile().write(JSON.stringify(current));
}

export async function removeSelection(widgetId: number): Promise<void> {
  const current = await readSelections();
  delete current[String(widgetId)];
  selectionsFile().write(JSON.stringify(current));
}
