import { useEffect } from 'react';
import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '../services/endpoints';
import { ApiError } from '../services/apiClient';
import { useAuthStore } from '../store/authStore';
import { useConnectivityStore } from '../store/connectivityStore';
import { entriesForUser, useOfflineQueueStore } from '../store/offlineQueueStore';
import { AnalyticsEvent, ErrorReason, track } from '../services/analytics';
import { invalidateAfterQuickEntry } from './queries';

// Módulo, no un ref de un solo hook: `flushOfflineQueue` también lo llama a
// mano el botón "Reintentar" del banner (PendingSyncBanner.tsx), así que la
// guarda contra corridas simultáneas tiene que ser compartida, no local a
// este hook.
let syncing = false;

/**
 * Manda en orden cada entrada de la cola de "registrado sin señal"
 * (services/offlineStorage.ts) mientras el envío tenga éxito, deteniéndose
 * en el primer fallo -- ver el comentario dentro del bucle. Reutiliza el
 * mismo `clientRequestId` con el que cada una se encoló (§36): si la app se
 * cerró a medio envío y esto se reintenta después, el índice único del
 * servidor garantiza que nunca se duplique el movimiento.
 *
 * Exportada (no solo usada dentro de useOfflineSync) porque el botón
 * "Reintentar" del banner de pendientes necesita poder dispararla a mano,
 * no solo esperar a que cambie `isOnline` o el tamaño de la cola.
 */
export async function flushOfflineQueue(client: QueryClient): Promise<void> {
  if (syncing) {
    return;
  }

  const userId = useAuthStore.getState().user?.id;
  const queue = entriesForUser(useOfflineQueueStore.getState().entries, userId);
  if (queue.length === 0) {
    return;
  }

  syncing = true;
  let syncedAny = false;

  try {
    for (const entry of queue) {
      try {
        await api.quickEntry.create(entry.body);
        await useOfflineQueueStore.getState().remove(entry.localId);
        syncedAny = true;
        track(AnalyticsEvent.QuickEntrySyncedOffline);
      } catch (error) {
        // Se detiene en el primer fallo en vez de seguir con el resto de la
        // cola: si fue por señal (ApiError status 0), el resto va a fallar
        // igual; si fue un rechazo real del servidor (400 -- por ejemplo una
        // cuenta que ya no existe), seguir mandando las siguientes fuera de
        // orden sería más confuso que detenerse y dejarlo visible en el
        // banner con "Reintentar".
        const detail = error instanceof ApiError ? error.message : 'Sin conexión.';
        await useOfflineQueueStore.getState().markFailed(entry.localId, detail);
        track(AnalyticsEvent.QuickEntrySyncFailed, { reason: ErrorReason.NetworkError });
        break;
      }
    }
  } finally {
    syncing = false;
  }

  if (syncedAny) {
    invalidateAfterQuickEntry(client);
  }
}

/**
 * Dispara `flushOfflineQueue` en cuanto vuelve la conexión (o cuando cambia
 * cuántas entradas hay pendientes). Un solo lugar hace esto -- igual que
 * useWidgetSync -- montado una vez en app/_layout.tsx para toda la vida de
 * la app.
 */
export function useOfflineSync(): void {
  const client = useQueryClient();
  const isOnline = useConnectivityStore((state) => state.isOnline);
  const isAuthenticated = useAuthStore((state) => state.status === 'authenticated');
  const hydrated = useOfflineQueueStore((state) => state.hydrated);
  const userId = useAuthStore((state) => state.user?.id);
  // Cuenta, no el arreglo: `markFailed` también cambia `entries` (guarda el
  // intento fallido) sin agregar ni quitar nada, y ese cambio NO debe volver
  // a disparar el efecto -- si lo hiciera, un rechazo real del servidor
  // reintentaría en bucle infinito, cada intento fallido disparando el
  // siguiente. La cuenta sí cambia cuando de verdad hay algo nuevo que
  // sincronizar: una entrada encolada o quitada. Solo cuenta lo de esta
  // sesión (entriesForUser) -- la cola de otra persona en este mismo
  // teléfono no debe disparar nada mientras no sea quien tiene sesión.
  const pendingCount = useOfflineQueueStore((state) => entriesForUser(state.entries, userId).length);

  useEffect(() => {
    if (!hydrated || !isOnline || !isAuthenticated || pendingCount === 0) {
      return;
    }
    void flushOfflineQueue(client);
  }, [hydrated, isOnline, isAuthenticated, pendingCount, client]);
}
