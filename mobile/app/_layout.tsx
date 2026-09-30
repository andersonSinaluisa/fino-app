import { useEffect, useRef, useState } from 'react';
import { QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { Stack } from 'expo-router';
import { colors } from '../theme';
import { ToastProvider } from '../components/ui/Toast';
import { useAuthStore } from '../store/authStore';
import { useOnboardingStore } from '../store/onboardingStore';
import { useOfflineQueueStore } from '../store/offlineQueueStore';
import { startConnectivityWatch } from '../store/connectivityStore';
import { useWidgetSync } from '../hooks/useWidgetSync';
import { useOfflineSync } from '../hooks/useOfflineSync';
import { useAnalyticsBootstrap } from '../hooks/useAnalyticsBootstrap';

/**
 * Home-screen widgets (iOS/Android) need to be kept in sync for the whole
 * app lifetime, not just while a particular screen is mounted -- rendered
 * once inside QueryClientProvider (it needs TanStack Query) so it can react
 * to every login/logout and every mutation that already invalidates
 * `queryKeys.summary`. Renders nothing.
 */
function WidgetSync() {
  useWidgetSync();
  return null;
}

/**
 * Modo offline, "registrar sin señal": sincroniza la cola de movimientos
 * encolados por QuickCashEntrySheet.tsx en cuanto vuelve la conexión. Mismo
 * patrón que WidgetSync -- montado una vez, para toda la vida de la app.
 */
function OfflineSync() {
  useOfflineSync();
  return null;
}

/**
 * Product analytics. Montado una vez, como los dos anteriores: arranca el
 * servicio, mantiene la sesión (§14), identifica al usuario tras el login y
 * reporta las pantallas principales leyendo la ruta de expo-router, para que
 * ninguna pantalla tenga que llamar a `screen()` por su cuenta (§15).
 *
 * Va DENTRO del Stack porque `usePathname()` necesita el contexto del router.
 * No pinta nada.
 */
function AnalyticsBootstrap() {
  useAnalyticsBootstrap();
  return null;
}

/**
 * Sube en 1 cada vez que cambia la FORMA de lo que se persiste (nuevos
 * campos que rompen el tipo, una query que deja de cachearse). El
 * persistidor descarta todo lo guardado bajo un buster distinto en vez de
 * arriesgarse a hidratar datos con una forma vieja -- la app simplemente
 * empieza sin caché offline hasta el próximo fetch real, nunca revienta.
 */
const OFFLINE_CACHE_BUSTER = 'v1';

/**
 * Qué se persiste en disco para verse offline (Inicio, Estadísticas, Pulsos,
 * el bootstrap del registro rápido). Deliberadamente NO incluye el historial
 * de movimientos (paginado, cambia constantemente, no es lo que alguien
 * necesita ver sin señal) ni nada de /auth (sesiones, actividad) -- los
 * tokens en sí nunca pasan por aquí, viven en SecureStore, pero mantener
 * esta lista corta es la misma disciplina de "solo lo que de verdad hace
 * falta offline" en vez de volcar el caché entero a disco sin pensarlo.
 */
const OFFLINE_QUERY_KEY_PREFIXES = new Set(['summary', 'accounts', 'categories', 'pulses', 'analytics', 'quick-entry']);

export default function RootLayout() {
  const restore = useAuthStore((state) => state.restore);
  const restoreOnboarding = useOnboardingStore((state) => state.restore);
  const restoreOfflineQueue = useOfflineQueueStore((state) => state.restore);
  const authStatus = useAuthStore((state) => state.status);

  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Financial data is worth a quick refetch, not an aggressive one.
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  const [persister] = useState(() =>
    createAsyncStoragePersister({ storage: AsyncStorage, key: 'fino.queryCache' }),
  );

  useEffect(() => {
    void restore();
    void restoreOnboarding();
    void restoreOfflineQueue();
    startConnectivityWatch();
  }, [restore, restoreOnboarding, restoreOfflineQueue]);

  // Modo offline: un logout de verdad (no el estado inicial "loading" del
  // arranque) borra el caché persistido. Sin esto, alguien que cierra sesión
  // y otra persona entra en el mismo teléfono vería, por un instante, el
  // resumen financiero de quien salió -- antes de esta persistencia en
  // disco eso desaparecía solo al reiniciar la app; ahora que sobrevive un
  // reinicio, hay que borrarlo explícitamente en el logout.
  const wasAuthenticated = useRef(false);
  useEffect(() => {
    if (authStatus === 'authenticated') {
      wasAuthenticated.current = true;
      return;
    }
    if (authStatus === 'anonymous' && wasAuthenticated.current) {
      wasAuthenticated.current = false;
      client.clear();
    }
  }, [authStatus, client]);

  return (
    <SafeAreaProvider>
      <PersistQueryClientProvider
        client={client}
        persistOptions={{
          persister,
          buster: OFFLINE_CACHE_BUSTER,
          // Un caché de más de un día no vale la pena mostrarlo como "última
          // foto conocida" -- mejor la pantalla vacía/de carga normal que un
          // saldo de ayer sin ningún aviso de qué tan viejo es.
          maxAge: 24 * 60 * 60 * 1000,
          dehydrateOptions: {
            shouldDehydrateQuery: (query) => {
              const [namespace] = query.queryKey;
              return typeof namespace === 'string' && OFFLINE_QUERY_KEY_PREFIXES.has(namespace);
            },
          },
        }}
      >
        <WidgetSync />
        <OfflineSync />
        <AnalyticsBootstrap />
        <StatusBar style="dark" />
        {/* §6: el toast con Deshacer tiene que sobrevivir a la navegación -- se
            muestra justo cuando el sheet se cierra, así que no puede vivir dentro
            de él. Envuelve toda la app por eso, no por comodidad. */}
        <ToastProvider>
        <Stack
          screenOptions={{
            headerShown: false,
            contentStyle: { backgroundColor: colors.background },
            animation: 'slide_from_right',
          }}
        >
          <Stack.Screen name="index" />
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(onboarding)" />
          <Stack.Screen name="(tabs)" />
          <Stack.Screen name="movimiento/[id]" options={{ presentation: 'card' }} />
          {/* §22: "Más detalles". El formulario completo del registro manual. */}
          <Stack.Screen name="movimiento/nuevo" options={{ presentation: 'modal' }} />
          {/* §25: corregir el efectivo dejando rastro. */}
          <Stack.Screen name="efectivo/ajustar" options={{ presentation: 'modal' }} />
          {/* §28-30: la dirección única del registro rápido desde fuera de la app
              (widgets, atajos, App Intents, Siri). No pinta nada: abre el sheet. */}
          <Stack.Screen name="registrar" options={{ presentation: 'transparentModal', animation: 'none' }} />
          <Stack.Screen name="cuentas/agregar" options={{ presentation: 'modal' }} />
          <Stack.Screen name="cuentas/importar" options={{ presentation: 'modal' }} />
          <Stack.Screen name="cuentas/importaciones" options={{ presentation: 'card' }} />
          <Stack.Screen name="cuentas/actualizar-saldo" options={{ presentation: 'modal' }} />
          <Stack.Screen name="cuentas/conciliacion" options={{ presentation: 'card' }} />
          <Stack.Screen name="cuentas/reconectar" options={{ presentation: 'modal' }} />
          <Stack.Screen name="dinero-disponible/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="plan/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="presupuestos/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="presupuestos/[id]" options={{ presentation: 'card' }} />
          <Stack.Screen name="comprometido/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="analisis-gastos/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="transferencias/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="alertas-financieras/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="conectar-correo/index" options={{ presentation: 'modal' }} />
          <Stack.Screen name="notificaciones/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="pulso/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="pulso/[id]" options={{ presentation: 'card' }} />
          <Stack.Screen name="sesiones/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="actividad/index" options={{ presentation: 'card' }} />
          <Stack.Screen name="compartir" options={{ presentation: 'modal' }} />
          {/* Escaneo de facturas. Modal porque es un flujo con principio y fin,
              igual que importar: se entra, se resuelve y se vuelve a donde se
              estaba. */}
          <Stack.Screen name="factura/index" options={{ presentation: 'modal' }} />
          <Stack.Screen name="reglas-categorizacion/index" options={{ presentation: 'modal' }} />
        </Stack>
        </ToastProvider>
      </PersistQueryClientProvider>
    </SafeAreaProvider>
  );
}
