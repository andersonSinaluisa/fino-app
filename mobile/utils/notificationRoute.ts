import type { Href } from 'expo-router';

/**
 * Where a notification takes the person, from its deep-link data (the push
 * `data` and the history row's `data` carry the same keys; see
 * NotificationDispatcher.BuildPushData). Null means "nothing more specific than
 * the notification list".
 */
export type NotificationData = Record<string, string | undefined> | null | undefined;

export function notificationRoute(data: NotificationData): Href | null {
  if (!data) {
    return null;
  }
  if (data.transactionId) {
    return `/movimiento/${data.transactionId}` as Href;
  }
  if (data.pulseId) {
    return `/pulso/${data.pulseId}` as Href;
  }
  if (data.cardId) {
    return `/tarjetas/${data.cardId}` as Href;
  }
  if (data.budgetId) {
    return `/presupuestos/${data.budgetId}` as Href;
  }
  if (data.accountId) {
    return { pathname: '/cuentas/importar', params: { accountId: data.accountId } } as Href;
  }
  if (data.screen === 'estadisticas') {
    return '/(tabs)/estadisticas' as Href;
  }
  return null;
}

/** Analytics only gets the KIND of notification opened, never its text or amounts. */
export function notificationKind(data: NotificationData): string {
  if (!data) {
    return 'other';
  }
  if (data.transactionId) return 'movement';
  if (data.pulseId) return 'pulse';
  if (data.cardId) return 'card';
  if (data.budgetId) return 'budget';
  if (data.accountId) return 'account';
  if (data.screen) return 'summary';
  return 'other';
}
