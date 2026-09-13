import { create } from 'zustand';
import NetInfo, { type NetInfoState } from '@react-native-community/netinfo';

/**
 * Modo offline: una sola fuente de verdad sobre si hay conexión real, para
 * que tanto la UI (banner "sin conexión", el botón Guardar del quick entry)
 * como código fuera de React (useOfflineSync.ts) pregunten aquí en vez de
 * suscribirse cada uno a NetInfo por su cuenta.
 */
interface ConnectivityState {
  isOnline: boolean;
}

export const useConnectivityStore = create<ConnectivityState>(() => ({
  // Optimista al arrancar: casi siempre hay señal, y si no la hay el primer
  // evento de NetInfo (casi inmediato) corrige esto en milisegundos -- mejor
  // eso que parpadear "sin conexión" en cada apertura de la app.
  isOnline: true,
}));

function deriveIsOnline(state: NetInfoState): boolean {
  if (state.isConnected === false) {
    return false;
  }
  // `isInternetReachable` puede quedar en `null` ("todavía no se sabe") un
  // instante tras conectar a una red -- se trata como online para no marcar
  // "sin conexión" mientras NetInfo todavía está probando la salida real a
  // internet (importa en wifis con portal cautivo).
  return state.isInternetReachable !== false;
}

let unsubscribe: (() => void) | null = null;

/** Llamado una vez desde app/_layout.tsx. Segura de llamar más de una vez (idempotente). */
export function startConnectivityWatch(): void {
  if (unsubscribe) {
    return;
  }
  unsubscribe = NetInfo.addEventListener((state) => {
    useConnectivityStore.setState({ isOnline: deriveIsOnline(state) });
  });
}
