import { create } from 'zustand';
import { createRequestId } from '../utils/quickEntry/requestId';
import { readOfflineQueue, writeOfflineQueue, type PendingQuickEntry } from '../services/offlineStorage';
import type { CreateQuickTransactionRequest } from '../types/api';

interface OfflineQueueState {
  entries: PendingQuickEntry[];
  /** False hasta que restore() termina de leer AsyncStorage -- evita que la
   * UI muestre "0 pendientes" por un instante en cada arranque frío. */
  hydrated: boolean;

  /** Llamado una vez, en app/_layout.tsx, junto a authStore.restore(). */
  restore: () => Promise<void>;

  /**
   * Encola un movimiento que no se pudo mandar por falta de señal.
   * `clientRequestId` lo decide quien llama (QuickCashEntrySheet ya genera
   * uno por intento de guardado, §36) para que sea EL MISMO id si la persona
   * reintenta -- nunca uno nuevo por reintento. `userId` es de quien tiene
   * la sesión activa ahora mismo -- ver el comentario de `userId` en
   * PendingQuickEntry (services/offlineStorage.ts) sobre por qué importa.
   */
  enqueue: (
    body: CreateQuickTransactionRequest,
    clientRequestId: string,
    userId: string,
  ) => Promise<PendingQuickEntry>;

  /** Quita una entrada -- tras sincronizarla, o si la persona la deshace localmente. */
  remove: (localId: string) => Promise<void>;

  /** Registra un intento de sincronización fallido sin quitar la entrada de la cola. */
  markFailed: (localId: string, message: string) => Promise<void>;
}

/**
 * Solo las entradas de `userId` -- ver el comentario de `userId` en
 * PendingQuickEntry sobre por qué la cola nunca se sincroniza ni se cuenta
 * "a ciegas" para toda la cola, solo para la sesión activa.
 */
export function entriesForUser(entries: PendingQuickEntry[], userId: string | undefined): PendingQuickEntry[] {
  if (!userId) {
    return [];
  }
  return entries.filter((entry) => entry.userId === userId);
}

export const useOfflineQueueStore = create<OfflineQueueState>((set, get) => ({
  entries: [],
  hydrated: false,

  restore: async () => {
    const entries = await readOfflineQueue();
    set({ entries, hydrated: true });
  },

  enqueue: async (body, clientRequestId, userId) => {
    const entry: PendingQuickEntry = {
      localId: createRequestId(),
      userId,
      clientRequestId,
      body,
      queuedAt: new Date().toISOString(),
      attempts: 0,
      lastError: null,
    };

    const entries = [...get().entries, entry];
    set({ entries });
    await writeOfflineQueue(entries);
    return entry;
  },

  remove: async (localId) => {
    const entries = get().entries.filter((entry) => entry.localId !== localId);
    set({ entries });
    await writeOfflineQueue(entries);
  },

  markFailed: async (localId, message) => {
    const entries = get().entries.map((entry) =>
      entry.localId === localId ? { ...entry, attempts: entry.attempts + 1, lastError: message } : entry,
    );
    set({ entries });
    await writeOfflineQueue(entries);
  },
}));
