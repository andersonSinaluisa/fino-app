import { create } from 'zustand';
import type { NotificationPreferences } from '../types/api';

interface DeviceState {
  /**
   * Entregable 17: the Expo push token this installation registered with, so
   * the notification-preferences screen can address `PUT
   * /notifications/devices/{token}/preferences` without asking Expo for it a
   * second time. Null until usePushRegistration finishes (or on a simulator,
   * or if the person denied the permission) -- push, and therefore per-device
   * preferences, is an enhancement, never a requirement to use the app.
   */
  expoPushToken: string | null;
  /**
   * The preferences this device last got back from the backend -- either at
   * registration (login/app start) or the last time the person changed them
   * here. There is no GET endpoint for a single device's preferences, so this
   * cached copy is what seeds the preferences screen.
   */
  preferences: NotificationPreferences | null;
  setDevice: (token: string, preferences: NotificationPreferences) => void;
  setPreferences: (preferences: NotificationPreferences) => void;
  /** Entregable 18: called on logout, after best-effort unregistering with the backend. */
  reset: () => void;
}

export const useDeviceStore = create<DeviceState>((set) => ({
  expoPushToken: null,
  preferences: null,
  setDevice: (expoPushToken, preferences) => set({ expoPushToken, preferences }),
  setPreferences: (preferences) => set({ preferences }),
  reset: () => set({ expoPushToken: null, preferences: null }),
}));
