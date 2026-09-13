import { useCallback, useEffect, useRef } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import { usePathname, useRouter } from 'expo-router';
import { readPendingSharedFile } from '../lib/shareImport';
import { useAuthStore } from '../store/authStore';
import { devLog } from '../services/devLog';

/**
 * Recoge un archivo que la hoja de compartir dejó esperando.
 *
 * EXISTE POR UNA LIMITACIÓN DE iOS, no por gusto. Una Share Extension no puede
 * abrir su app contenedora: la documentación de Apple dice que
 * `NSExtensionContext.open` solo lo soportan los puntos de extensión Today e
 * iMessage. Así que el deep link `fino:///compartir` nunca llegaba a dispararse
 * en iOS, `app/compartir.tsx` no se montaba nunca, y compartir un Excel con Fino
 * se veía como una ventana en blanco que aparecía y se cerraba.
 *
 * La solución es invertir el relevo: la extensión deja el archivo en el App Group
 * y la app lo busca por su cuenta al arrancar y cada vez que vuelve a primer
 * plano -- que es exactamente cuando la persona acaba de compartir algo y está
 * volviendo a Fino.
 *
 * En Android NO estorba: allí `plugins/withShareIntent.js` reescribe el intent
 * como un VIEW deep link que MainActivity sí recibe, así que la navegación normal
 * suele ganar la carrera; y si no, este hook llega al mismo sitio. En cualquier
 * caso `readPendingSharedFile` es una lectura barata y no destructiva -- quien
 * borra la marca es `app/compartir.tsx` cuando la persona elige la cuenta.
 */
export function usePendingShare(): void {
  const router = useRouter();
  const pathname = usePathname();
  const authStatus = useAuthStore((state) => state.status);

  /**
   * El último archivo por el que ya navegamos. Sin esto, cada vuelta a primer
   * plano volvería a empujar /compartir mientras la marca siga puesta, y la
   * persona no podría salir de esa pantalla.
   */
  const routedFor = useRef<string | null>(null);
  const pathnameRef = useRef(pathname);
  pathnameRef.current = pathname;

  const check = useCallback(async () => {
    if (authStatus !== 'authenticated') {
      return;
    }

    // Ya está en la pantalla: no hay nada que empujar.
    if (pathnameRef.current === '/compartir') {
      return;
    }

    const pending = await readPendingSharedFile();

    if (!pending) {
      // La marca se limpió (se importó o se descartó): se permite volver a
      // navegar si en el futuro llega otro archivo con el mismo nombre.
      routedFor.current = null;
      return;
    }

    if (routedFor.current === pending.uri) {
      return;
    }

    routedFor.current = pending.uri;
    devLog('share', 'pending_file_found', { name: pending.name });
    router.push({ pathname: '/compartir', params: { file: pending.name } });
  }, [authStatus, router]);

  useEffect(() => {
    // Al montar: cubre el caso de compartir con Fino cerrada del todo.
    void check();

    const subscription = AppState.addEventListener('change', (next: AppStateStatus) => {
      if (next === 'active') {
        void check();
      }
    });

    return () => subscription.remove();
  }, [check]);
}
