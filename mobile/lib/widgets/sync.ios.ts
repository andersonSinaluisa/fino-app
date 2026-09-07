import { ExtensionStorage } from '@bacons/apple-targets';
import { WIDGET_APP_GROUP, WIDGET_SNAPSHOT_KEY } from './constants';
import type { WidgetSnapshot } from './types';

const storage = new ExtensionStorage(WIDGET_APP_GROUP);

/**
 * Pushes the snapshot into the App Group's shared UserDefaults and asks
 * WidgetKit to reload every Fino widget's timeline. This is the only place
 * in the app that talks to `@bacons/apple-targets` -- every widget's own
 * Swift code (targets/widget/Snapshot.swift) only ever reads what this
 * writes, never anything else.
 */
export function pushWidgetSnapshot(snapshot: WidgetSnapshot): void {
  storage.set(WIDGET_SNAPSHOT_KEY, JSON.stringify(snapshot));
  ExtensionStorage.reloadWidget();
}
