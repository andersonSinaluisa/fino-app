import { useEffect, useRef } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import { usePathname } from 'expo-router';
import { analytics } from '../services/analytics/service';
import { AnalyticsEvent, AnalyticsSource, type AnalyticsScreenName } from '../services/analytics/events';
import { resolveScreen } from '../services/analytics/screens';
import { useAuthStore } from '../store/authStore';

/**
 * Arranca analytics y mantiene sesión, identidad y pantallas.
 *
 * Se monta UNA vez, en el layout raíz. No pinta nada.
 */
export function useAnalyticsBootstrap(): void {
  const pathname = usePathname();
  const status = useAuthStore((state) => state.status);
  const user = useAuthStore((state) => state.user);

  const lastScreen = useRef<AnalyticsScreenName | null>(null);
  const identifiedId = useRef<string | null>(null);

  // Arranque + primera sesión.
  useEffect(() => {
    let cancelled = false;

    const start = async () => {
      await analytics.init();

      if (cancelled) {
        return;
      }

      const isNewSession = await analytics.noteAppActive();

      if (cancelled) {
        return;
      }

      analytics.track(AnalyticsEvent.AppOpened, { source: AnalyticsSource.Unknown });

      if (isNewSession) {
        analytics.track(AnalyticsEvent.SessionStarted, { source: AnalyticsSource.Unknown });
      }
    };

    void start();

    return () => {
      cancelled = true;
    };
  }, []);

  // §14: una vuelta del background solo abre sesión nueva si la pausa superó
  // la ventana de inactividad. Sin esto, mirar una notificación y volver
  // contaría como una sesión más y la retención saldría inflada.
  useEffect(() => {
    const subscription = AppState.addEventListener('change', (next: AppStateStatus) => {
      if (next === 'active') {
        void analytics.noteAppActive().then((isNewSession) => {
          analytics.track(AnalyticsEvent.AppOpened, { source: AnalyticsSource.Unknown });

          if (isNewSession) {
            analytics.track(AnalyticsEvent.SessionStarted, { source: AnalyticsSource.Unknown });
          }
        });
        return;
      }

      if (next === 'background' || next === 'inactive') {
        analytics.track(AnalyticsEvent.AppBackgrounded);
        void analytics.noteAppInactive();
      }
    });

    return () => subscription.remove();
  }, []);

  // §3: identificar al entrar, cortar al salir.
  useEffect(() => {
    if (status === 'authenticated' && user?.id) {
      if (identifiedId.current === user.id) {
        return;
      }

      identifiedId.current = user.id;
      // §5: solo propiedades de producto. `onboarding` viene del backend y ya
      // trae exactamente estas dos señales, así que no hay que inferir nada.
      analytics.identify(user.id, {
        onboardingCompleted: Boolean(user.onboarding?.firstImportCompletedAt ?? user.onboarding?.skippedAt),
        hasImportedStatement: Boolean(user.onboarding?.hasImportedData),
        locale: user.locale,
      });
      return;
    }

    if (status === 'anonymous' && identifiedId.current) {
      identifiedId.current = null;
      analytics.reset();
    }
  }, [status, user]);

  // Pantallas.
  useEffect(() => {
    const next = resolveScreen(pathname);

    if (!next || next === lastScreen.current) {
      return;
    }

    lastScreen.current = next;
    analytics.screen(next);
  }, [pathname]);
}
