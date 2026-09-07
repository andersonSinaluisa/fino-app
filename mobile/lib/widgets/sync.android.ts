import { requestWidgetUpdate } from 'react-native-android-widget';
import { ANDROID_WIDGET_NAMES, type AndroidWidgetName } from './constants';
import { readSelection, writeSnapshotFile } from './storage';
import type { WidgetSnapshot } from './types';
import { renderAndroidWidget } from './android/widgets';

const ALL_WIDGET_NAMES: AndroidWidgetName[] = Object.values(ANDROID_WIDGET_NAMES);

/**
 * Persists the snapshot to disk (so the headless task handler can read it on
 * the next OS-triggered update -- see taskHandler.ts) and, for every widget
 * of every kind currently on the home screen, pushes a fresh render right
 * now. `requestWidgetUpdate` calls its `renderWidget` once per instance
 * already on the home screen and is a no-op for a widget kind with none.
 */
export function pushWidgetSnapshot(snapshot: WidgetSnapshot): void {
  writeSnapshotFile(JSON.stringify(snapshot));

  for (const widgetName of ALL_WIDGET_NAMES) {
    void requestWidgetUpdate({
      widgetName,
      renderWidget: async (widgetInfo) => {
        const selectedId = await readSelection(widgetInfo.widgetId);
        return renderAndroidWidget(widgetName, snapshot, selectedId);
      },
    });
  }
}
