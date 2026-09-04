import { useEffect } from 'react';
import { Platform } from 'react-native';
import * as Notifications from 'expo-notifications';
import * as Device from 'expo-device';
import Constants from 'expo-constants';
import { api } from '../services/endpoints';
import { useAuthStore } from '../store/authStore';

/**
 * Registers this installation for push once the user is signed in.
 *
 * Everything here is best effort: a simulator, a denied permission or a missing
 * EAS project id must never block the app. Push is an enhancement, not a
 * dependency of the product working.
 */
export function usePushRegistration(): void {
  const status = useAuthStore((state) => state.status);

  useEffect(() => {
    if (status !== 'authenticated') {
      return;
    }

    let cancelled = false;

    const register = async () => {
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

      await api.notifications.registerDevice({
        expoPushToken: token.data,
        platform: Platform.OS === 'ios' ? 'Ios' : Platform.OS === 'android' ? 'Android' : 'Web',
        deviceName: Device.deviceName ?? null,
        appVersion: Constants.expoConfig?.version ?? null,
      });
    };

    register().catch(() => {
      // Silence: a failed push registration is not worth interrupting anyone.
    });

    return () => {
      cancelled = true;
    };
  }, [status]);
}
