/**
 * Shared identifiers both the JS side and the native widget code need to
 * agree on byte-for-byte. Keep this the single source of truth -- app.json
 * (iOS entitlements), targets/widget/expo-target.config.js, and the Android
 * config plugin entry in app.json all read from copies of these same
 * literal values (native config files can't `import` TS), and the sync
 * modules import this file directly.
 */

/** iOS App Group shared between the main app and the widget extension. */
export const WIDGET_APP_GROUP = 'group.app.fino.mobile';

/** UserDefaults / SharedPreferences key the JSON-encoded WidgetSnapshot is stored under. */
export const WIDGET_SNAPSHOT_KEY = 'snapshot';

/**
 * Android widget provider names -- must match the `name` given to each
 * widget entry in app.json's `react-native-android-widget` plugin config,
 * which in turn becomes each `<widgetName>` used by `requestWidgetUpdate`
 * and by the task handler's `props.widgetInfo.widgetName` switch.
 */
export const ANDROID_WIDGET_NAMES = {
  availableMoney: 'DineroDisponible',
  totalBalance: 'SaldoTotal',
  nextPayment: 'ProximoPago',
  monthExpenses: 'GastosDelMes',
  categorySpend: 'Presupuesto',
  projection: 'Proyeccion',
  account: 'Cuenta',
  pulse: 'Pulso',
  /**
   * §28: el único widget que no muestra un dato -- es un botón para registrar.
   * Comparte el registro y el manejador de los demás para no abrir una segunda
   * forma de dibujar widgets, pero ignora el snapshot: no tiene nada que leer.
   */
  quickEntry: 'RegistrarEfectivo',
} as const;

export type AndroidWidgetName = (typeof ANDROID_WIDGET_NAMES)[keyof typeof ANDROID_WIDGET_NAMES];
