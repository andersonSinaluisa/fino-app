import type { WidgetSnapshot } from './types';

/**
 * Fallback for any platform other than iOS/Android (this app does not ship
 * a web widget surface) -- Metro picks `sync.ios.ts`/`sync.android.ts`
 * automatically for those platforms via React Native's platform-extension
 * resolution (see tsconfig.json's `moduleSuffixes`), so this file's real
 * job is just to give `import { pushWidgetSnapshot } from './sync'` a type
 * on platforms where no widget surface exists.
 */
export function pushWidgetSnapshot(_snapshot: WidgetSnapshot): void {
  // No-op: no widget surface on this platform.
}
