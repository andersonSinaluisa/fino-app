import { useEffect } from 'react';
import { Platform } from 'react-native';
import { useRouter } from 'expo-router';
import * as Device from 'expo-device';
import Constants from 'expo-constants';
import { api } from '../services/endpoints';
import { useAuthStore } from '../store/authStore';
import { useDeviceStore } from '../store/deviceStore';

type NotificationsModule = typeof import('expo-notifications');

let notificationsPromise: Promise<NotificationsModule | null> | null = null;

async function loadNotifications(): Promise<NotificationsModule | null> {
  if (Constants.appOwnership === 'expo' || Constants.expoGoConfig || Constants.expoVersion) {
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
 * Registers this installation for push once the user is signed in.
 *
 * Everything here is best effort: a simulator, a denied permission or a missing
 * EAS project id must never block the app. Push is an enhancement, not a
 * dependency of the product working.
 */
export function usePushRegistration(): void {
  const router = useRouter();
  const status = useAuthStore((state) => state.status);
  const setDevice = useDeviceStore((state) => state.setDevice);

  useEffect(() => {
    if (status !== 'authenticated') {
      return;
    }

    let cancelled = false;

    const register = async () => {
      const Notifications = await loadNotifications();

      if (!Notifications) {
        return;
      }

      if (!Device.isDevice) {
        return;
      }

      const existing = await Notifications.getPermissionsAsync();
      let granted = existing.granted;

      if (!granted && existing.canAskAgain) {
        const requested = await Notifications.requestPermissionsAsync();
        granted = requested.granted;
      }

      if (!granted || cancelled) {
        return;
      }

      const projectId =
        (Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined)?.eas?.projectId;

      const token = await Notifications.getExpoPushTokenAsync(projectId ? { projectId } : undefined);

      if (cancelled || !token.data) {
        return;
      }

      const preferences = await api.notifications.registerDevice({
        expoPushToken: token.data,
        platform: Platform.OS === 'ios' ? 'Ios' : Platform.OS === 'android' ? 'Android' : 'Web',
        deviceName: Device.deviceName ?? null,
        appVersion: Constants.expoConfig?.version ?? null,
      });

      // Entregable 17: the preferences screen needs this same token to call
      // PUT .../devices/{token}/preferences, and there is no GET to fetch the
      // current values on its own -- both are stashed here at registration time.
      setDevice(token.data, preferences);
    };

    register().catch(() => {
      // Silence: a failed push registration is not worth interrupting anyone.
    });

    return () => {
      cancelled = true;
    };
  }, [status, setDevice]);

  useEffect(() => {
    // Entregable 18 ("Push end-to-end"): closes the loop from "a push arrived"
    // to "the app is now looking at the right thing". `notificationId` always
    // rides along (see NotificationDispatcher.BuildPushData) and is enough to
    // mark it read; `transactionId` is only present for movement/income
    // pushes and, when there, deep-links straight to that movement instead of
    // just opening the notification list.
    let cancelled = false;
    let subscription: { remove: () => void } | null = null;

    void loadNotifications().then((Notifications) => {
      if (!Notifications || cancelled) {
        return;
      }

      subscription = Notifications.addNotificationResponseReceivedListener((response) => {
        const data = response.notification.request.content.data as Record<string, string> | undefined;
        const notificationId = data?.notificationId;
        const transactionId = data?.transactionId;

        if (notificationId) {
          api.notifications.markRead(notificationId).catch(() => undefined);
        }

        if (transactionId) {
          router.push(`/movimiento/${transactionId}`);
        } else {
          router.push('/notificaciones');
        }
      });
    });

    return () => {
      cancelled = true;
      subscription?.remove();
    };
  }, [router]);
}
