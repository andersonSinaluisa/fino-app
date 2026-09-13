import { ExtensionStorage } from '@bacons/apple-targets';
import { WIDGET_APP_GROUP } from '../widgets/constants';
import type { PendingQuickEntryIntent } from './pendingIntent.types';

export type { PendingQuickEntryIntent } from './pendingIntent.types';

/** Debe coincidir byte a byte con QuickEntryHandoff.key en QuickEntryIntent.swift. */
const PENDING_INTENT_KEY = 'pendingQuickEntryIntent';

/**
 * Cuánto vale una marca antes de considerarse vieja.
 *
 * Sin esto, alguien que pidiera el atajo a Siri, cambiara de idea y abriera Fino
 * dos horas después se encontraría el sheet abierto sin haberlo pedido. Dos minutos
 * cubren de sobra el tiempo entre "oye Siri" y que la app termine de arrancar.
 */
const MAX_AGE_SECONDS = 120;

interface StoredPayload {
  mode?: string;
  requestedAt?: number;
}

const storage = new ExtensionStorage(WIDGET_APP_GROUP);

export function readPendingQuickEntryIntent(): PendingQuickEntryIntent | null {
  const raw = storage.get(PENDING_INTENT_KEY) as StoredPayload | string | null | undefined;

  if (!raw || typeof raw === 'string') {
    return null;
  }

  const requestedAt = typeof raw.requestedAt === 'number' ? raw.requestedAt : 0;
  const ageSeconds = Date.now() / 1000 - requestedAt;

  if (!Number.isFinite(ageSeconds) || ageSeconds > MAX_AGE_SECONDS || ageSeconds < -MAX_AGE_SECONDS) {
    // Marca vieja (o un reloj imposible): se descarta en vez de abrir el sheet de
    // la nada.
    clearPendingQuickEntryIntent();
    return null;
  }

  return { mode: raw.mode === 'voice' ? 'voice' : 'keypad' };
}

export function clearPendingQuickEntryIntent(): void {
  storage.remove(PENDING_INTENT_KEY);
}
