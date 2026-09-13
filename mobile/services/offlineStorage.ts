import AsyncStorage from '@react-native-async-storage/async-storage';
import type { CreateQuickTransactionRequest } from '../types/api';

/**
 * Modo offline -- "registrar sin señal" (§ análisis de sesión sobre por qué
 * el teléfono a veces pedía iniciar sesión al recuperar conexión, que llevó
 * a esto). Persistencia plana en AsyncStorage, NO en SecureStore: esto no
 * son credenciales, son movimientos que la persona ya escribió y que solo
 * faltan por mandarse -- perderlos por un cierre de la app sería peor que
 * guardarlos sin cifrar.
 *
 * Un solo movimiento pendiente por vez es lo normal, pero la cola soporta
 * varios (varias entradas seguidas sin señal) y los sincroniza en orden --
 * ver hooks/useOfflineSync.ts.
 */

const QUEUE_KEY = 'fino.offlineQuickEntryQueue';

export interface PendingQuickEntry {
  /** Solo local -- nunca viaja al servidor, solo identifica la fila para la UI/cola. */
  localId: string;
  /**
   * Dueño de este movimiento (AuthenticatedUser.id). En un teléfono compartido,
   * alguien puede quedar offline a medio registrar, cerrar sesión ANTES de
   * recuperar señal, y otra persona iniciar sesión después -- sin esto, la
   * sincronización mandaría el movimiento con la sesión de quien esté
   * conectada en ese momento, no de quien lo escribió. flushOfflineQueue y
   * PendingSyncBanner solo consideran las entradas de la sesión activa (ver
   * `entriesForUser` en offlineQueueStore.ts); las de otra persona quedan en
   * espera hasta que esa persona vuelva a iniciar sesión.
   */
  userId: string;
  /**
   * El mismo valor que se manda como `CreateQuickTransactionRequest.clientRequestId`
   * -- la clave de idempotencia de §36 (índice único (UserId, ClientRequestId) en
   * MoneyConfigurations.cs). Por eso un reintento de sincronización -- la app
   * cerrada a medio envío, una petición que sí llegó pero cuya respuesta se
   * perdió -- nunca puede duplicar el movimiento: el servidor devuelve el que
   * ya creó para este id en vez de crear otro.
   */
  clientRequestId: string;
  body: CreateQuickTransactionRequest;
  queuedAt: string;
  attempts: number;
  lastError: string | null;
}

export async function readOfflineQueue(): Promise<PendingQuickEntry[]> {
  try {
    const raw = await AsyncStorage.getItem(QUEUE_KEY);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? (parsed as PendingQuickEntry[]) : [];
  } catch {
    // Un JSON corrupto no debe tumbar el arranque de la app -- se pierde la
    // cola (raro, y ya son movimientos que la persona puede volver a
    // escribir) en vez de dejar la app en un estado roto.
    return [];
  }
}

export async function writeOfflineQueue(queue: PendingQuickEntry[]): Promise<void> {
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
}
