/**
 * Every widget tap opens a real, existing Fino screen -- these are the only
 * deep links widgets ever produce. Built on top of the app's existing
 * `scheme: "fino"` (app.json) and expo-router's file-based routes, so no new
 * linking configuration is required.
 *
 * Format: Expo's documented deep-link shape is `scheme:///path` (three
 * slashes -- the empty host is intentional, see
 * https://docs.expo.dev/guides/linking/). react-native-android-widget's
 * built-in `OPEN_URI` click action and the iOS widget's `Link`/`widgetURL`
 * both just need a valid URL string, so this file is the single source of
 * truth both platforms read from (Android reads it directly in JS; the iOS
 * value travels inside the snapshot written to ExtensionStorage).
 */

const SCHEME = 'fino';

function link(path: string): string {
  return `${SCHEME}:///${path}`;
}

export const widgetDeepLinks = {
  home: () => link(''),
  accounts: () => link('cuentas'),
  statistics: () => link('estadisticas'),
  movementsByCategory: (categoryId: string) => link(`movimientos?categoryId=${encodeURIComponent(categoryId)}`),
  movementsByAccount: (accountId: string) => link(`movimientos?accountId=${encodeURIComponent(accountId)}`),
  /** app/pulso/[id].tsx -- the same detail screen the in-app Pulso history list already opens. */
  pulso: (pulseId: string) => link(`pulso/${encodeURIComponent(pulseId)}`),

  /**
   * §28-30: registro rápido desde fuera de la app. Un único enlace para el widget
   * de Android, el de iOS, el atajo de mantener pulsado el icono, un App Intent de
   * iOS y Siri. Todos abren la MISMA pantalla (app/registrar.tsx), que a su vez
   * abre el MISMO sheet: ninguno de esos puntos de entrada tiene su propia lógica
   * de creación, que es lo que §28 pide separando CreateQuickTransaction de la UI.
   */
  quickEntry: () => link('registrar'),

  /** §13: el mismo enlace, pero abriendo ya con el micrófono escuchando. */
  quickEntryByVoice: () => link('registrar?voz=1'),
};
