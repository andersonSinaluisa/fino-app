/**
 * Catálogo central de analytics.
 *
 * Toda la app reporta a través de este catálogo. No existe ningún otro sitio
 * donde se pueda inventar el nombre de un evento: `AnalyticsEventName` es la
 * unión de los valores de `AnalyticsEvent`, así que un string suelto no
 * compila.
 *
 * Los valores son snake_case porque es lo que espera el proveedor externo;
 * las claves son PascalCase para usarlas tipadas desde el código.
 *
 * REGLA: esto describe COMPORTAMIENTO DE PRODUCTO. Ningún evento y ninguna
 * propiedad puede llevar montos, saldos, descripciones, comercios, nombres,
 * correos, identificadores bancarios ni contenido de archivos o de voz. El
 * gate real lo aplica `schema.ts` (allowlist por evento) + `sanitize.ts`.
 */

/**
 * Sube en 1 cuando cambia la FORMA de los eventos (se renombra una propiedad,
 * cambia el significado de un bucket). Viaja en cada evento como
 * `schemaVersion` para que un dashboard pueda distinguir datos viejos de
 * nuevos en vez de mezclarlos en silencio. Documentar el cambio en
 * docs/analytics.md.
 */
export const ANALYTICS_SCHEMA_VERSION = 1;

export const AnalyticsEvent = {
  // --- Ciclo de vida -------------------------------------------------------
  AppOpened: 'app_opened',
  AppBackgrounded: 'app_backgrounded',
  SessionStarted: 'session_started',

  // --- Autenticación -------------------------------------------------------
  LoginStarted: 'login_started',
  LoginSucceeded: 'login_succeeded',
  LoginFailed: 'login_failed',
  SignupStarted: 'signup_started',
  SignupSucceeded: 'signup_succeeded',
  SignupFailed: 'signup_failed',
  Logout: 'logout',

  // --- Onboarding ----------------------------------------------------------
  OnboardingStarted: 'onboarding_started',
  OnboardingStepViewed: 'onboarding_step_viewed',
  OnboardingSkipped: 'onboarding_skipped',
  OnboardingCompleted: 'onboarding_completed',

  // --- Elección de banco y tutorial ----------------------------------------
  BankSelected: 'bank_selected',
  BankTutorialStarted: 'bank_tutorial_started',
  BankTutorialStepViewed: 'bank_tutorial_step_viewed',
  BankTutorialCompleted: 'bank_tutorial_completed',

  // --- Importación ---------------------------------------------------------
  FilePickerOpened: 'file_picker_opened',
  ImportStarted: 'import_started',
  ImportCompleted: 'import_completed',
  ImportFailed: 'import_failed',

  // --- Navegación principal ------------------------------------------------
  HomeViewed: 'home_viewed',
  MovementsViewed: 'movements_viewed',
  MovementOpened: 'movement_opened',

  // --- Registro rápido de efectivo -----------------------------------------
  QuickEntryOpened: 'quick_entry_opened',
  QuickEntrySaved: 'quick_entry_saved',
  QuickEntryUndo: 'quick_entry_undo',
  QuickEntryFailed: 'quick_entry_failed',
  QuickEntryDetailsOpened: 'quick_entry_details_opened',
  QuickEntryQueuedOffline: 'quick_entry_queued_offline',
  QuickEntryUndoOffline: 'quick_entry_undo_offline',
  QuickEntrySyncedOffline: 'quick_entry_synced_offline',
  QuickEntrySyncFailed: 'quick_entry_sync_failed',
  FrequentEntryUsed: 'frequent_entry_used',

  // --- Entrada inteligente (texto natural) ---------------------------------
  SmartEntryUsed: 'smart_entry_used',
  SmartEntryParsed: 'smart_entry_parsed',
  SmartEntryFailed: 'smart_entry_failed',

  // --- Voz -----------------------------------------------------------------
  VoiceEntryStarted: 'voice_entry_started',
  VoiceEntryParsed: 'voice_entry_parsed',
  VoiceEntryFailed: 'voice_entry_failed',
  VoicePermissionDenied: 'voice_permission_denied',

  // --- Cuenta efectivo -----------------------------------------------------
  CashAccountCreated: 'cash_account_created',
  CashBalanceSet: 'cash_balance_set',
  CashBalanceAdjusted: 'cash_balance_adjusted',

  // --- Conciliación --------------------------------------------------------
  ReconciliationViewed: 'reconciliation_viewed',
  TransferSuggestionShown: 'transfer_suggestion_shown',
  TransferConfirmed: 'transfer_confirmed',
  TransferRejected: 'transfer_rejected',
  WithdrawalDetected: 'withdrawal_detected',
  WithdrawalReviewed: 'withdrawal_reviewed',
  WithdrawalConfirmedAsCash: 'withdrawal_confirmed_as_cash',
  WithdrawalRejected: 'withdrawal_rejected',

  // --- Estadísticas --------------------------------------------------------
  StatisticsViewed: 'statistics_viewed',
  StatisticsPeriodChanged: 'statistics_period_changed',
  StatisticsAccountFilterChanged: 'statistics_account_filter_changed',

  // --- Presupuestos y Comprometido ----------------------------------------
  // Describen el USO de la función. Ningún monto, nombre de presupuesto,
  // categoría personalizada ni porcentaje sale de aquí.
  BudgetCreated: 'budget_created',
  BudgetUpdated: 'budget_updated',
  BudgetDeleted: 'budget_deleted',
  BudgetReserveEnabled: 'budget_reserve_enabled',
  BudgetExceeded: 'budget_exceeded',
  BudgetOpened: 'budget_opened',
  CommittedBreakdownOpened: 'committed_breakdown_opened',

  // --- Movimientos divididos ----------------------------------------------
  TransactionSplitCreated: 'transaction_split_created',
  TransactionSplitUpdated: 'transaction_split_updated',
  TransactionSplitRemoved: 'transaction_split_removed',

  // --- Pulso ---------------------------------------------------------------
  PulseCardViewed: 'pulse_card_viewed',
  PulseOpened: 'pulse_opened',
  PulseActionClicked: 'pulse_action_clicked',
  PulseDismissed: 'pulse_dismissed',
  PulseFeedback: 'pulse_feedback',

  // --- Notificaciones ------------------------------------------------------
  NotificationPermissionShown: 'notification_permission_shown',
  NotificationPermissionAccepted: 'notification_permission_accepted',
  NotificationPermissionRejected: 'notification_permission_rejected',
  NotificationOpened: 'notification_opened',

  // --- Proyección y cuentas ------------------------------------------------
  ProjectionViewed: 'projection_viewed',
  AccountAdded: 'account_added',
  AccountUpdated: 'account_updated',
  ProfileViewed: 'profile_viewed',

  // --- Suscripción ---------------------------------------------------------
  SubscriptionScreenViewed: 'subscription_screen_viewed',
  SubscriptionStarted: 'subscription_started',
  SubscriptionCompleted: 'subscription_completed',
  SubscriptionFailed: 'subscription_failed',

  // --- Escaneo de facturas (§37) -------------------------------------------
  ReceiptScanOpened: 'receipt_scan_opened',
  ReceiptCameraOpened: 'receipt_camera_opened',
  ReceiptGalleryOpened: 'receipt_gallery_opened',
  ReceiptCaptureCompleted: 'receipt_capture_completed',
  ReceiptAnalysisStarted: 'receipt_analysis_started',
  ReceiptAnalysisCompleted: 'receipt_analysis_completed',
  ReceiptAnalysisFailed: 'receipt_analysis_failed',
  ReceiptReviewOpened: 'receipt_review_opened',
  ReceiptSaved: 'receipt_saved',
  ReceiptMatchedExistingTransaction: 'receipt_matched_existing_transaction',
  ReceiptPossibleDuplicateShown: 'receipt_possible_duplicate_shown',
  ReceiptRetry: 'receipt_retry',

  // --- Hitos únicos por usuario (ver trackOnce) ----------------------------
  FirstValueReached: 'first_value_reached',
  FirstImportCompleted: 'first_import_completed',
  FirstCashEntry: 'first_cash_entry',
  FirstPulseOpened: 'first_pulse_opened',
  FirstReceiptScanned: 'first_receipt_scanned',

  // --- Errores de producto (NO reemplaza a un error monitor) ---------------
  AppError: 'app_error',
} as const;

export type AnalyticsEventName = (typeof AnalyticsEvent)[keyof typeof AnalyticsEvent];

/**
 * Pantallas principales. Deliberadamente NO se registran los modales
 * pequeños (§15): un `screen()` por cada hoja que se abre y se cierra
 * ensucia el embudo y no responde ninguna pregunta.
 */
export const AnalyticsScreen = {
  Login: 'login',
  Register: 'register',
  Onboarding: 'onboarding',
  Home: 'home',
  Movements: 'movements',
  Statistics: 'statistics',
  Accounts: 'accounts',
  Profile: 'profile',
  PulseList: 'pulse_list',
  PulseDetail: 'pulse_detail',
  Reconciliation: 'reconciliation',
  Import: 'import',
  Notifications: 'notifications',
  Projection: 'projection',
  Subscription: 'subscription',
  Budgets: 'budgets',
  BudgetDetail: 'budget_detail',
  Committed: 'committed',
} as const;

export type AnalyticsScreenName = (typeof AnalyticsScreen)[keyof typeof AnalyticsScreen];

/** Cómo llegó el movimiento. Dimensión de producto, sin dato financiero. */
export const EntryMode = {
  Keypad: 'keypad',
  SmartText: 'smart_text',
  Voice: 'voice',
  Frequent: 'frequent',
  Recent: 'recent',
  FullForm: 'full_form',
} as const;

export type EntryModeValue = (typeof EntryMode)[keyof typeof EntryMode];

/** Desde dónde se abrió un flujo. */
export const AnalyticsSource = {
  Home: 'home',
  Movements: 'movements',
  Accounts: 'accounts',
  Shortcut: 'shortcut',
  Widget: 'widget',
  Siri: 'siri',
  Share: 'share',
  Notification: 'notification',
  Onboarding: 'onboarding',
  Deeplink: 'deeplink',
  Budgets: 'budgets',
  Committed: 'committed',
  Unknown: 'unknown',
} as const;

export type AnalyticsSourceValue = (typeof AnalyticsSource)[keyof typeof AnalyticsSource];

export const TransactionDirection = {
  Expense: 'expense',
  Income: 'income',
} as const;

export const CategorySource = {
  Auto: 'auto',
  Manual: 'manual',
  Uncategorized: 'uncategorized',
} as const;

/**
 * §18: nunca `durationMs: 1874`. Los buckets responden la pregunta
 * ("¿el registro rápido baja de 3 segundos?") sin convertir una duración en
 * un identificador casi único por usuario.
 */
export const DurationBucket = {
  Under2s: 'under_2s',
  From2To5s: '2_5s',
  From5To10s: '5_10s',
  From10To30s: '10_30s',
  Over30s: 'over_30s',
} as const;

export type DurationBucketValue = (typeof DurationBucket)[keyof typeof DurationBucket];

/** §9: cuántos movimientos trajo una importación, en tramos. Nunca el número. */
export const CountBucket = {
  Zero: '0',
  OneToTen: '1_10',
  ElevenToFifty: '11_50',
  FiftyOneToHundred: '51_100',
  HundredOnePlus: '101_plus',
} as const;

export type CountBucketValue = (typeof CountBucket)[keyof typeof CountBucket];

/** §12/§5: tramos para "cuántas fuentes conectadas tiene". */
export const SourceCountBucket = {
  Zero: '0',
  One: '1',
  Two: '2',
  ThreePlus: '3_plus',
} as const;

/** §19: razones sanitizadas. Nunca el mensaje de la excepción ni el stack. */
export const ErrorReason = {
  UnsupportedFormat: 'unsupported_format',
  ParserFailed: 'parser_failed',
  UploadError: 'upload_error',
  NetworkError: 'network_error',
  PermissionDenied: 'permission_denied',
  NotRecognized: 'not_recognized',
  Empty: 'empty',
  Timeout: 'timeout',
  Cancelled: 'cancelled',
  ServerError: 'server_error',
  Unknown: 'unknown',
} as const;

export type ErrorReasonValue = (typeof ErrorReason)[keyof typeof ErrorReason];

/** §11: de dónde vino el primer valor real que vio el usuario. */
export const ValueSource = {
  StatementImport: 'statement_import',
  CashManual: 'cash_manual',
} as const;

/** §37: qué tan bien salió el análisis, sin decir qué se leyó. */
export const AnalysisResult = {
  Complete: 'complete',
  Partial: 'partial',
  Failed: 'failed',
} as const;

export type AnalysisResultValue = (typeof AnalysisResult)[keyof typeof AnalysisResult];

export const FileFormat = {
  Xlsx: 'xlsx',
  Xls: 'xls',
  Csv: 'csv',
  Pdf: 'pdf',
  Other: 'other',
} as const;

export type FileFormatValue = (typeof FileFormat)[keyof typeof FileFormat];

/** Presupuestos: el período elegido (conjunto cerrado), nunca fechas ni montos. */
export const BudgetPeriodValue = {
  Weekly: 'weekly',
  Biweekly: 'biweekly',
  Monthly: 'monthly',
  Custom: 'custom',
} as const;

/** Presupuestos: dónde se activó la reserva. */
export const BudgetFlow = {
  Create: 'create',
  Edit: 'edit',
} as const;
