import { useCallback, useEffect } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import {
  clearPendingQuickEntryIntent,
  readPendingQuickEntryIntent,
} from '../lib/quickEntry/pendingIntent';
import { useQuickEntryStore } from '../store/quickEntryStore';
import { useAuthStore } from '../store/authStore';
import { devLog } from '../services/devLog';

/**
 * §29: recoge la marca que dejó un App Intent de iOS y abre el sheet.
 *
 * Un App Intent no puede abrir una pantalla concreta de la app: puede pedir que la
 * app pase a primer plano (`openAppWhenRun`) y poco más. Así que el intent deja una
 * marca en el App Group y esto la recoge -- exactamente el mismo relevo invertido
 * que usa la extensión de compartir, y por la misma razón: es lo único que iOS
 * soporta de verdad.
 */
export function usePendingIntent(): void {
  const authStatus = useAuthStore((state) => state.status);
  const open = useQuickEntryStore((state) => state.open);

  const check = useCallback(() => {
    if (authStatus !== 'authenticated') {
      return;
    }

    const pending = readPendingQuickEntryIntent();

    if (!pending) {
      return;
    }

    // Se consume inmediatamente: la marca vale para una apertura, no para todas las
    // veces que la app vuelva a primer plano.
    clearPendingQuickEntryIntent();
    devLog('quickEntry', 'intent_handoff', { mode: pending.mode });
    open('shortcut', { withVoice: pending.mode === 'voice' });
  }, [authStatus, open]);

  useEffect(() => {
    check();

    const subscription = AppState.addEventListener('change', (next: AppStateStatus) => {
      if (next === 'active') {
        check();
      }
    });

    return () => subscription.remove();
  }, [check]);
}
