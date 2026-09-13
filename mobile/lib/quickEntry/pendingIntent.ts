import type { PendingQuickEntryIntent } from './pendingIntent.types';

export type { PendingQuickEntryIntent } from './pendingIntent.types';

/**
 * §29: la marca que deja un App Intent de iOS ("Registrar gasto en Fino") para que
 * la app abra el sheet al pasar a primer plano.
 *
 * Existe solo en iOS. En Android el mismo trabajo lo hace el atajo del icono, que sí
 * puede abrir un deep link directamente (plugins/withQuickEntryShortcut.js), así que
 * no hace falta ningún relevo por almacenamiento compartido.
 *
 * Metro elige pendingIntent.ios.ts automáticamente en iOS; este archivo es el
 * fallback para las demás plataformas, con la misma firma para que tsc no vea una
 * discrepancia bajo `moduleSuffixes`.
 */
export function readPendingQuickEntryIntent(): PendingQuickEntryIntent | null {
  return null;
}

export function clearPendingQuickEntryIntent(): void {
  // No-op: sin App Intents en esta plataforma.
}
