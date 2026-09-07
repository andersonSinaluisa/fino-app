import type { WidgetTaskHandler } from 'react-native-android-widget';
import type { AndroidWidgetName } from '../constants';
import { readSelection, readSnapshotFile, removeSelection } from '../storage';
import { emptySnapshot, type WidgetSnapshot } from '../types';
import { renderAndroidWidget } from './widgets';

async function loadSnapshot(): Promise<WidgetSnapshot> {
  const raw = await readSnapshotFile();
  if (!raw) {
    return emptySnapshot();
  }
  try {
    return JSON.parse(raw) as WidgetSnapshot;
  } catch {
    return emptySnapshot();
  }
}

/**
 * Handles every OS-triggered widget lifecycle event (added to home screen,
 * periodic `updatePeriodMillis` refresh, resized, removed). Runs in a fresh,
 * headless JS context with no access to the running app's React Query cache
 * or Zustand stores -- so, deliberately, it never fetches anything itself
 * and only reads the snapshot file the live app already wrote (storage.ts).
 * Registered in index.js alongside `registerWidgetConfigurationScreen`.
 */
export const widgetTaskHandler: WidgetTaskHandler = async (props) => {
  const { widgetInfo, widgetAction, renderWidget } = props;
  const widgetName = widgetInfo.widgetName as AndroidWidgetName;

  switch (widgetAction) {
    case 'WIDGET_ADDED':
    case 'WIDGET_UPDATE':
    case 'WIDGET_RESIZED': {
      const snapshot = await loadSnapshot();
      const selectedId = await readSelection(widgetInfo.widgetId);
      renderWidget(renderAndroidWidget(widgetName, snapshot, selectedId));
      break;
    }
    case 'WIDGET_DELETED':
      // Forget this instance's category/account pick -- nothing left to clean up otherwise.
      await removeSelection(widgetInfo.widgetId);
      break;
    case 'WIDGET_CLICK':
      // Every tap in this app uses the built-in OPEN_APP/OPEN_URI click
      // actions (see WidgetCard.tsx), which the library resolves natively
      // without ever reaching this handler -- kept only for completeness.
      break;
    default:
      break;
  }
};
