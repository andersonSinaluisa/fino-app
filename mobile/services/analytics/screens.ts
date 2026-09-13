import { AnalyticsScreen, type AnalyticsScreenName } from './events';

/**
 * §15: el mapa ruta -> pantalla, aparte del hook a propósito.
 *
 * Es lógica pura: no importa expo-router ni React, así que se puede probar
 * sin montar nada. El hook (hooks/useAnalyticsBootstrap.ts) solo lee la ruta
 * actual y pregunta aquí.
 *
 * Una ruta que no esté en este mapa NO genera evento de pantalla. Los
 * modales pequeños (ajustar saldo, agregar cuenta, compartir, registrar) no
 * son pantallas a efectos de producto: contarlos ensucia los embudos y hace
 * que "pantallas por sesión" deje de significar nada.
 */
const SCREEN_BY_PATH: Record<string, AnalyticsScreenName> = {
  '/': AnalyticsScreen.Home,
  '/login': AnalyticsScreen.Login,
  '/register': AnalyticsScreen.Register,
  '/onboarding': AnalyticsScreen.Onboarding,
  '/bienvenida': AnalyticsScreen.Onboarding,
  '/como-funciona': AnalyticsScreen.Onboarding,
  '/banco': AnalyticsScreen.Onboarding,
  '/conectar': AnalyticsScreen.Onboarding,
  '/movimientos': AnalyticsScreen.Movements,
  '/estadisticas': AnalyticsScreen.Statistics,
  '/cuentas': AnalyticsScreen.Accounts,
  '/perfil': AnalyticsScreen.Profile,
  '/pulso': AnalyticsScreen.PulseList,
  '/notificaciones': AnalyticsScreen.Notifications,
  '/transferencias': AnalyticsScreen.Reconciliation,
  '/cuentas/conciliacion': AnalyticsScreen.Reconciliation,
  '/cuentas/importar': AnalyticsScreen.Import,
  '/dinero-disponible': AnalyticsScreen.Projection,
  '/plan': AnalyticsScreen.Subscription,
};

export function resolveScreen(pathname: string): AnalyticsScreenName | null {
  const direct = SCREEN_BY_PATH[pathname];
  if (direct) {
    return direct;
  }

  // `/pulso/<id>` -- el id nunca viaja, solo el hecho de estar en el detalle.
  if (pathname.startsWith('/pulso/')) {
    return AnalyticsScreen.PulseDetail;
  }

  return null;
}
