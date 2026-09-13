/**
 * Onboarding funcional: forma de configuración por banco. La UI (BankTutorial,
 * BankTutorialAnimation) se genera enteramente a partir de esto -- nunca
 * `if (bankId === 'guayaquil')` regado por la app. Agregar o corregir un banco
 * es agregar/editar un archivo en este directorio, no tocar ninguna pantalla.
 *
 * `bankId` es el mismo código que ya usa el backend (Nexo.Domain.Providers.ProviderCodes,
 * p.ej. "GUAYAQUIL") -- así el registro se resuelve directamente desde
 * `Provider.code` (useProviders()), sin mantener una segunda lista de bancos.
 */

/** Una "pantalla" genérica del banco que FakeBankScreen sabe dibujar -- ver ese componente. */
export type BankTutorialScreen = 'accounts' | 'account-detail' | 'statements' | 'period' | 'download';

export interface BankTutorialStep {
  screen: BankTutorialScreen;
  /** Qué fila/botón resalta AnimatedTap + HighlightArea en esta pantalla. */
  target: string;
  /** Texto bajo la animación (TutorialCaption). Una sola frase, sin jerga. */
  instruction: string;
}

export interface BankTutorialConfig {
  bankId: string;
  /** Nombre para mostrar -- puede diferir levemente del `Provider.name` del backend (p. ej. más corto). */
  title: string;
  /**
   * Se sube cada vez que `steps` cambia de verdad (la app del banco cambió de
   * menú, etc.) -- ver docs del backend sobre versionado; hoy vive solo en el
   * cliente, pero el campo existe para no tener que rediseñar esto el día que
   * las instrucciones se sirvan desde el backend.
   */
  tutorialVersion: number;
  /**
   * true solo cuando alguien de producto de verdad abrió la app actual de
   * este banco y confirmó estos pasos paso a paso. Hoy todos los bancos
   * arrancan en false a propósito -- los 4 pasos genéricos (cuentas →
   * estados de cuenta → periodo → descargar) son la estructura real que
   * describe el parser y el formato exportado, pero los nombres exactos de
   * menú dentro de cada app NO se han verificado contra capturas reales
   * todavía, así que no se afirma una fecha de verificación falsa.
   */
  verified: boolean;
  /** Cuándo se escribió/actualizó por última vez esta configuración (no lo mismo que "verificado" -- ver arriba). */
  lastUpdated: string;
  steps: BankTutorialStep[];
}

export interface BankImportConfig {
  bankId: string;
  displayName: string;
  /**
   * Extensiones reales que el parser de este banco acepta -- hoy los cuatro
   * bancos soportados comparten el mismo conjunto (ver
   * backend/src/Nexo.Application/Imports/Parsing/Parsers y
   * mobile/lib/importFileTypes.ts), pero queda por banco porque no hay
   * garantía de que siga siendo así. Nunca se muestra un formato aquí que
   * ACCEPTED_EXTENSIONS (lib/importFileTypes.ts) no acepte también --
   * ver bankTutorials/index.ts, que lo valida en desarrollo.
   */
  supportedFormats: Array<'csv' | 'xls' | 'xlsx'>;
  /** Una frase honesta sobre qué exportar -- nunca "no necesitas modificarlo" si el banco en cuestión sí lo requiere. */
  fileHint: string;
  tutorial: BankTutorialConfig;
}
