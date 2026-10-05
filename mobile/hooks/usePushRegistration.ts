import { useEffect } from 'react';
import { Platform } from 'react-native';
import { useRouter } from 'expo-router';
import * as Device from 'expo-device';
import Constants, { ExecutionEnvironment } from 'expo-constants';
import { api } from '../services/endpoints';
import { useAuthStore } from '../store/authStore';
import { useDeviceStore } from '../store/deviceStore';
import { AnalyticsEvent, AnalyticsSource, track } from '../services/analytics';
import type { NotificationPreferences } from '../types/api';
import { notificationKind, notificationRoute } from '../utils/notificationRoute';

type NotificationsModule = typeof import('expo-notifications');

let notificationsPromise: Promise<NotificationsModule | null> | null = null;

async function loadNotifications(): Promise<NotificationsModule | null> {
  // Solo Expo Go carece de push real. En un build de EAS (TestFlight / Play)
  // executionEnvironment es "standalone" o "bare".
  if (Constants.executionEnvironment === ExecutionEnvironment.StoreClient) {
    return null;
  }

  notificationsPromise ??= import('expo-notifications')
    .then((notifications) => {
      /**
       * Without a handler, a notification that arrives while the app is open is
       * swallowed. "Compra detectada" is exactly the moment the user is most
       * likely to be looking at the screen, so it has to show.
       */
      notifications.setNotificationHandler({
        handleNotification: async () => ({
          shouldShowBanner: true,
          shouldShowList: true,
          shouldPlaySound: false,
          shouldSetBadge: false,
        }),
      });

      return notifications;
    })
    .catch(() => null);

  return notificationsPromise;
}

/**
 * Resultado de intentar registrar este dispositivo para push:
 * - registered: permiso concedido y token guardado en el backend.
 * - unsupported: simulador o Expo Go (sin push real).
 * - denied: la persona dijo que no ahora; se puede volver a preguntar.
 * - blocked: iOS/Android ya no deja preguntar; solo desde los Ajustes del teléfono.
 * - error: algo falló (por ejemplo, el build no tiene el permiso de push de Apple).
 */
export type PushRegistrationResult =
  | { status: 'registered' }
  | { status: 'unsupported' }
  | { status: 'denied' }
  | { status: 'blocked' }
  | { status: 'error'; message: string };

/**
 * Pide permiso (si se puede) y registra el token de este dispositivo. La usa el
 * registro automático al iniciar sesión y el botón "Activar notificaciones" de
 * Perfil → Notificaciones, que necesita saber POR QUÉ no se pudo para decirlo.
 */
export async function registerForPushAsync(
  setDevice: (token: string, preferences: NotificationPreferences) => void,
  source: (typeof AnalyticsSource)[keyof typeof AnalyticsSource] = AnalyticsSource.Home,
): Promise<PushRegistrationResult> {
  const Notifications = await loadNotifications();
  if (!Notifications || !Device.isDevice) {
    return { status: 'unsupported' };
  }

  try {
    const existing = await Notifications.getPermissionsAsync();
    let granted = existing.granted || existing.ios?.status === Notifications.IosAuthorizationStatus.PROVISIONAL;

    if (!granted) {
      if (!existing.canAskAgain) {
        return { status: 'blocked' };
      }

      // §16: la tasa de opt-in a notificaciones es una métrica de producto
      // por sí sola, y sin el evento "se mostró" el denominador no existe.
      track(AnalyticsEvent.NotificationPermissionShown, { source });
      const requested = await Notifications.requestPermissionsAsync({
        ios: { allowAlert: true, allowBadge: true, allowSound: true },
      });
      granted = requested.granted;
      track(
        granted ? AnalyticsEvent.NotificationPermissionAccepted : AnalyticsEvent.NotificationPermissionRejected,
        { source },
      );

      if (!granted) {
        return { status: requested.canAskAgain ? 'denied' : 'blocked' };
      }
    }

    if (Platform.OS === 'android') {
      // Android 8+: sin canal no hay sonido ni banner con prioridad alta.
      await Notifications.setNotificationChannelAsync('default', {
        name: 'Notificaciones de Fino',
        importance: Notifications.AndroidImportance.HIGH,
      });
    }

    const projectId =
      (Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined)?.eas?.projectId;
    const token = await Notifications.getExpoPushTokenAsync(projectId ? { projectId } : undefined);
    if (!token.data) {
      return { status: 'error', message: 'No se obtuvo el token de notificaciones.' };
    }

    const preferences = await api.notifications.registerDevice({
      expoPushToken: token.data,
      platform: Platform.OS === 'ios' ? 'Ios' : Platform.OS === 'android' ? 'Android' : 'Web',
      deviceName: Device.deviceName ?? null,
      appVersion: Constants.expoConfig?.version ?? null,
    });

    // Entregable 17: la pantalla de preferencias necesita este mismo token
    // para PUT .../devices/{token}/preferences.
    setDevice(token.data, preferences);
    return { status: 'registered' };
  } catch (error) {
    return { status: 'error', message: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Registers this installation for push once the user is signed in.
 *
 * Best effort: a simulator, a denied permission or a missing EAS project id
 * must never block the app. Push is an enhancement, not a dependency of the
 * product working.
 */
export function usePushRegistration(): void {
  const router = useRouter();
  const status = useAuthStore((state) => state.status);
  const setDevice = useDeviceStore((state) => state.setDevice);

  useEffect(() => {
    if (status !== 'authenticated') {
      return;
    }

    void registerForPushAsync(setDevice);
  }, [status, setDevice]);

  useEffect(() => {
    // Entregable 18 ("Push end-to-end"): closes the loop from "a push arrived"
    // to "the app is now looking at the right thing". `notificationId` always
    // rides along (see NotificationDispatcher.BuildPushData) and is enough to
    // mark it read; `transactionId` is only present for movement/income
    // pushes and, when there, deep-links straight to that movement instead of
    // just opening the notification list. PULSO FASE 3: `pulseId` rides along
    // on PulseReady pushes (see PulseNotificationDecisionService) and
    // deep-links to that pulse's detail instead. Recordatorios add `cardId`,
    // `budgetId`, `accountId` and `screen` (see utils/notificationRoute).
    let cancelled = false;
    let subscription: { remove: () => void } | null = null;

    void loadNotifications().then((Notifications) => {
      if (!Notifications || cancelled) {
        return;
      }

      subscription = Notifications.addNotificationResponseReceivedListener((response) => {
        const data = response.notification.request.content.data as Record<string, string> | undefined;
        const notificationId = data?.notificationId;

        // §25 (Dashboard 5): sin este evento no hay PULSE_OPEN_RATE. Viaja el
        // TIPO de notificación, nunca su título ni su cuerpo -- el cuerpo de
        // un push de FINO puede llevar un monto.
        track(AnalyticsEvent.NotificationOpened, { notificationKind: notificationKind(data) });

        if (notificationId) {
          api.notifications.markRead(notificationId).catch(() => undefined);
        }

        router.push(notificationRoute(data) ?? '/notificaciones');
      });
    });

    return () => {
      cancelled = true;
      subscription?.remove();
    };
  }, [router]);
}
