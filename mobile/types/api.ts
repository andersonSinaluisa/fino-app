/** Mirrors the DTOs in Nexo.Application. Keep both sides in sync deliberately. */

export type TransactionDirection = 'Income' | 'Expense';
export type TransactionSource = 'Import' | 'Email' | 'Api' | 'Webhook' | 'Manual';
export type TransactionStatus = 'Posted' | 'Pending' | 'NeedsReview' | 'Ignored';
export type BalanceType = 'Verified' | 'Estimated';
export type ConnectionMode = 'ManualImport' | 'Email' | 'Api' | 'Webhook';
export type AccountType = 'Checking' | 'Savings' | 'CreditCard' | 'Wallet' | 'Other';

export interface AuthenticatedUser {
  id: string;
  email: string;
  displayName: string;
  timeZoneId: string;
  currency: string;
  locale: string;
}

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthenticatedUser;

  /**
   * Entregable 22 ("Privacidad completa"): true when this login undid a
   * pending "eliminar mi cuenta" request that was still inside its grace
   * period. Always false otherwise, including on refresh/register.
   */
  accountDeletionCancelled: boolean;
}

export interface ProviderCapability {
  mode: ConnectionMode;
  label: string;
  isAutomatic: boolean;
}

export interface Provider {
  code: string;
  name: string;
  shortName: string;
  kind: 'Bank' | 'Wallet';
  brandColor: string;
  logoKey: string | null;
  capabilities: ProviderCapability[];
  defaultMode: ConnectionMode;
  supportsStatementImport: boolean;
}

export interface Account {
  id: string;
  providerCode: string;
  providerName: string;
  brandColor: string;
  logoKey: string | null;
  alias: string;
  accountType: AccountType;
  mask: string | null;
  currency: string;
  connectionMode: ConnectionMode;
  balance: number;
  balanceType: BalanceType;
  lastVerifiedBalance: number | null;
  lastVerifiedAt: string | null;
  lastTransactionAt: string | null;
  lastSyncedAt: string | null;
  isArchived: boolean;
}

export interface TransactionListItem {
  id: string;
  financialAccountId: string;
  accountAlias: string;
  providerCode: string;
  brandColor: string;
  transactionDate: string;
  amount: number;
  signedAmount: number;
  currency: string;
  direction: TransactionDirection;
  description: string;
  merchant: string | null;
  categoryId: string | null;
  categoryName: string | null;
  categoryIcon: string | null;
  categoryColor: string | null;
  status: TransactionStatus;
  source: TransactionSource;
  /**
   * Entregable 13: true once the person confirmed this is one leg of a
   * transfer between their own accounts. It still moved the account's
   * balance, but the backend excludes it from net income/expense totals and
   * insights -- moving your own money is neither spending nor earning.
   */
  isInternalTransfer: boolean;
}

/**
 * "Categorización personal": por qué una transacción tiene la categoría que
 * tiene, para que la UI lo pueda explicar (punto 11) en vez de dejar que la
 * persona adivine.
 */
export type CategorySource = 'Uncategorized' | 'SystemRule' | 'UserRule' | 'Imported' | 'Manual';

export interface TransactionDetail extends Omit<TransactionListItem, 'brandColor' | 'categoryIcon' | 'categoryColor'> {
  providerName: string;
  accountMask: string | null;
  /**
   * Entregable 12: presente solo cuando la persona corrigió el comercio a
   * mano. `merchant` ya refleja la corrección cuando existe -- este campo es
   * metadata para la UI (mostrar el aviso "corregido", saber si aplica Borrar).
   */
  merchantCorrected: string | null;
  categoryManuallySet: boolean;
  /** "Categorización personal" (punto 11): de dónde salió la categoría actual. */
  categorySource: CategorySource;
  /** Presente solo cuando `categorySource` es `UserRule` o `SystemRule`. */
  categorizationRuleId: string | null;
  externalReference: string | null;
  sourceConfidence: 'Low' | 'Medium' | 'High';
  note: string | null;
  possibleDuplicateOfId: string | null;
  importId: string | null;
  createdAt: string;
  /** Entregable 13: the matching movement on the other account, once confirmed. */
  internalTransferLinkId: string | null;
  /**
   * "Categorización personal" (punto 7): presente solo en la respuesta de
   * PUT .../category cuando se pidió aplicar también a movimientos
   * anteriores -- cuántos OTROS movimientos se recategorizaron. `null` en
   * cualquier otra lectura de la transacción.
   */
  recategorizedCount: number | null;
}

/**
 * "Categorización personal": una regla propia del usuario (punto 3/15),
 * "Reglas de categorización" en Ajustes. Nunca incluye reglas de otro
 * usuario ni reglas del sistema (esas no tienen pantalla propia todavía).
 */
export interface CategorizationRule {
  id: string;
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  categoryId: string;
  categoryName: string;
  categoryIcon: string;
  categoryColor: string;
  isActive: boolean;
  priority: number;
  matchCount: number;
  createdAt: string;
  updatedAt: string;
  lastMatchedAt: string | null;
}

export interface CreateCategorizationRuleRequest {
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  categoryId: string;
}

export interface UpdateCategorizationRuleRequest {
  categoryId: string;
  isActive?: boolean;
  /** Punto 16: nunca se recategoriza el historial salvo que se pida explícitamente. */
  applyToExistingMatches?: boolean;
}

/** Punto 19: "si creo esta regla, ¿qué movimientos coincidirían?", antes de guardar nada. */
export interface RulePreviewRequest {
  transactionId: string;
  categoryId: string;
}

export interface RulePreviewTransaction {
  id: string;
  description: string;
  signedAmount: number;
  transactionDate: string;
}

export interface RulePreview {
  pattern: string;
  matchType: 'Contains' | 'StartsWith' | 'Exact';
  /** Punto 18: el patrón sugerido es demasiado genérico -- Nexo no ofrecerá crear la regla. */
  isTooGeneric: boolean;
  matchedCount: number;
  sample: RulePreviewTransaction[];
  /** Punto 13: ya existe una regla propia con este mismo patrón, para otra categoría. */
  conflictingRuleId: string | null;
  conflictingCategoryId: string | null;
  conflictingCategoryName: string | null;
}

/**
 * Entregable 13: one suggested pair -- an outflow from one account and an
 * inflow into another, same amount, close in time, probably the same money
 * moving between the person's own accounts.
 */
export interface InternalTransferCandidate {
  outgoingTransactionId: string;
  outgoingAccountId: string;
  outgoingAccountAlias: string;
  outgoingDate: string;
  outgoingDescription: string;
  incomingTransactionId: string;
  incomingAccountId: string;
  incomingAccountAlias: string;
  incomingDate: string;
  incomingDescription: string;
  amount: number;
  currency: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
  totalPages: number;
}

export interface Category {
  id: string;
  code: string;
  name: string;
  icon: string;
  color: string;
  isSystem: boolean;
  isIncome: boolean;
}

export interface CategoryBreakdownItem {
  categoryId: string;
  name: string;
  icon: string;
  color: string;
  total: number;
  percentage: number;
  count: number;
}

export interface Insight {
  code: string;
  title: string;
  body: string;
  value: number | null;
  comparisonValue: number | null;
  percentChange: number | null;
  severity: 'Neutral' | 'Positive' | 'Attention';
  referenceId: string | null;
  periodStart: string;
  periodEnd: string;
  /**
   * Entregable 16 ("Insights v1"): the backend already never returns an insight
   * past this instant, so the app has no need to check it itself -- it rides
   * along for API-shape completeness (e.g. a client that caches this response).
   */
  validUntil: string;
}

/**
 * Entregable 15 ("Dashboard final MVP"): "comparación mensual" as a guaranteed
 * dashboard figure. The percent fields are null when there is nothing from the
 * previous month to compare against.
 */
export interface MonthComparison {
  previousIncome: number;
  previousExpense: number;
  incomeChangePercent: number | null;
  expenseChangePercent: number | null;
}

/** Entregable 15: one entry per manually-imported account overdue for a sync. */
export interface StaleAccount {
  accountId: string;
  alias: string;
  lastSyncedAt: string | null;
}

export interface HomeSummary {
  greeting: string;
  displayName: string;
  totalBalance: number;
  currency: string;
  accountCount: number;
  anyEstimatedBalance: boolean;
  month: { income: number; expense: number; net: number };
  monthComparison: MonthComparison;
  accounts: Account[];
  recentTransactions: TransactionListItem[];
  categoryBreakdown: CategoryBreakdownItem[];
  staleAccounts: StaleAccount[];
  insights: Insight[];
}

export interface ImportPreviewRow {
  id: string;
  rowNumber: number;
  transactionDate: string | null;
  amount: number | null;
  direction: TransactionDirection | null;
  description: string | null;
  externalReference: string | null;
  status: 'Ready' | 'ExactDuplicate' | 'ProbableDuplicate' | 'Invalid' | 'Imported' | 'Skipped';
  matchType: 'NoMatch' | 'ProbableMatch' | 'ExactMatch';
  matchedTransactionId: string | null;
  suggestedCategoryId: string | null;
  suggestedCategoryName: string | null;
  error: string | null;
}

export interface ImportPreview {
  importId: string;
  financialAccountId: string;
  fileName: string;
  parserCode: string | null;
  status: 'Received' | 'PreviewReady' | 'Completed' | 'Failed' | 'Cancelled';
  totalRows: number;
  newRows: number;
  duplicateRows: number;
  probableDuplicateRows: number;
  invalidRows: number;
  incomeTotal: number;
  expenseTotal: number;
  periodStart: string | null;
  periodEnd: string | null;
  declaredClosingBalance: number | null;
  failureReason: string | null;
  /** True when an identical file was already imported into this account. */
  previouslyImportedFile: boolean;
  rows: ImportPreviewRow[];
  /**
   * Entregable 10 (mapeo manual de columnas): presentes solo cuando
   * status === 'Failed' porque ningún parser reconoció el archivo.
   * unmappedColumns es la primera fila muestreada del archivo (a menudo el
   * encabezado real, pero no se asume que lo sea); unmappedSampleRows son las
   * filas siguientes, para mostrar una grilla cruda y dejar que la persona
   * asigne cada columna a fecha/descripción/monto/etc.
   */
  unmappedColumns?: string[] | null;
  unmappedSampleRows?: string[][] | null;
}

/** Elección de columnas que la persona hace a mano para un archivo no reconocido. */
export interface ManualColumnMapping {
  firstRowIsHeader: boolean;
  dateColumn: number;
  descriptionColumn: number;
  amountColumn?: number | null;
  debitColumn?: number | null;
  creditColumn?: number | null;
  referenceColumn?: number | null;
  saveMapping: boolean;
}

export interface ImportResult {
  importId: string;
  importedCount: number;
  skippedDuplicates: number;
  flaggedForReview: number;
  newEstimatedBalance: number;
  balanceType: BalanceType;
  /** Entregable 27: of skippedDuplicates, how many folded their exact-match
   * statement data into an existing movement (typically one email created
   * earlier) instead of being a plain no-op skip. */
  upgradedCount: number;
}

/** One row of the import history screen (Entregable 8). */
export interface ImportSummary {
  importId: string;
  financialAccountId: string;
  accountAlias: string;
  fileName: string;
  status: 'Received' | 'PreviewReady' | 'Completed' | 'Failed' | 'Cancelled';
  createdAt: string;
  newRows: number;
  duplicateRows: number;
  probableDuplicateRows: number;
  invalidRows: number;
  importedCount: number;
}

export interface EmailConnection {
  id: string;
  providerKind: 'Gmail' | 'Outlook' | 'Forwarding';
  emailAddress: string;
  status: string;
  inboundAddress: string | null;
  connectedAt: string | null;
  lastSyncedAt: string | null;
  transactionsDetected: number;
  isAvailable: boolean;
  unavailableReason: string | null;
}

/**
 * Entregable 17 ("Notificaciones"): una categoría por cada tipo de aviso que la
 * persona puede prender o apagar de forma independiente -- Movimientos,
 * Ingresos, Insights, Seguridad, Recordatorios. `pushEnabled` y
 * `showAmountsInPreview` no son categorías: el primero apaga el push del todo
 * en este dispositivo, el segundo es la privacidad de la vista previa
 * (Entregable 18).
 */
export interface NotificationPreferences {
  pushEnabled: boolean;
  showAmountsInPreview: boolean;
  notifyOnMovements: boolean;
  notifyOnIncome: boolean;
  notifyOnInsights: boolean;
  notifyOnSecurity: boolean;
  notifyOnReminders: boolean;
}

/** Entregable 17: one row of the in-app notification list. */
export interface AppNotification {
  id: string;
  type: string;
  title: string;
  body: string;
  isRead: boolean;
  createdAt: string;
}

/**
 * Entregable 19 ("Sesiones y dispositivos"): one active refresh token, which --
 * since it is single-use and rotates on every use -- is exactly one device's
 * currently open session. `createdAt` is that row's own issuance time, so for
 * a session that has been quietly rotating in the background it reads as
 * "last active" rather than "logged in since".
 */
/**
 * Entregable 21 ("Auditoría"): one row of the user's own account activity.
 * `label` is the sentence to show -- already in Spanish, built server-side so
 * this type never has to duplicate the action-to-sentence mapping. `action` is
 * only the raw code (e.g. "user.logged_in"), kept around for picking an icon.
 */
export interface SecurityEvent {
  id: string;
  action: string;
  label: string;
  createdAt: string;
  succeeded: boolean;
}

export interface Session {
  id: string;
  deviceLabel: string | null;
  userAgent: string | null;
  createdAt: string;
  expiresAt: string;
  isCurrent: boolean;
}

/** Dashboard de estadísticas -- ventanas de tiempo que el backend entiende. */
export type AnalyticsPeriodCode = 'month' | 'last_month' | 'last_3_months' | 'last_6_months' | 'year' | 'custom';

export interface AnalyticsPeriodInfo {
  period: AnalyticsPeriodCode;
  label: string;
  from: string;
  to: string;
}

/**
 * Los 4 KPIs del dashboard. Los `*ChangePercent` son null cuando no hay nada
 * en el periodo anterior con qué comparar -- nunca un 0% inventado.
 */
export interface AnalyticsKpis {
  income: number;
  expense: number;
  net: number;
  savingsRatePercent: number | null;
  incomeChangePercent: number | null;
  expenseChangePercent: number | null;
  netChangePercent: number | null;
}

/**
 * Fijo/variable no es una suposición por categoría: un comercio solo cuenta
 * como fijo si de verdad se repitió en al menos 2 de los últimos 6 meses con
 * un monto parecido (ver `RecurringPayment`).
 */
export interface MoneyFlow {
  income: number;
  fixedExpense: number;
  variableExpense: number;
  netSavings: number;
  savingsRatePercent: number | null;
}

export interface AnalyticsSeriesPoint {
  from: string;
  to: string;
  label: string;
  income: number;
  expense: number;
}

/**
 * Un saldo real reconstruido a partir del saldo actual menos los movimientos
 * que pasaron después de ese momento -- nunca una proyección hacia adelante.
 */
export interface BalancePoint {
  asOf: string;
  label: string;
  balance: number;
}

export interface CategoryTrend {
  categoryId: string;
  name: string;
  icon: string;
  color: string;
  total: number;
  percentage: number;
  count: number;
  previousTotal: number | null;
  changePercent: number | null;
}

/** Un comercio que apareció en al menos 2 de los últimos 6 meses con un monto estable. */
export interface RecurringPayment {
  merchant: string;
  categoryName: string | null;
  averageAmount: number;
  occurrencesLast6Months: number;
  lastSeenAt: string;
}

export interface MerchantRanking {
  merchant: string;
  total: number;
  count: number;
}

export interface DailySpend {
  date: string;
  total: number;
}

export interface WeekdayWeekend {
  weekdayTotal: number;
  weekdayAveragePerDay: number;
  weekendTotal: number;
  weekendAveragePerDay: number;
}

export interface MonthlyHistoryItem {
  monthLabel: string;
  income: number;
  expense: number;
  net: number;
}

export interface AnalyticsDashboard {
  period: AnalyticsPeriodInfo;
  kpis: AnalyticsKpis;
  moneyFlow: MoneyFlow;
  series: AnalyticsSeriesPoint[];
  balanceEvolution: BalancePoint[];
  categoryBreakdown: CategoryTrend[];
  spotlightCategoryId: string | null;
  recurringPayments: RecurringPayment[];
  topMerchants: MerchantRanking[];
  peakSpendingDays: DailySpend[];
  weekdayWeekend: WeekdayWeekend;
  monthlyHistory: MonthlyHistoryItem[];
  insights: Insight[];
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
  /**
   * "Categorización personal" (punto 13): presentes solo cuando `code` es
   * `rule_conflict` -- la regla existente con la que choca el patrón nuevo,
   * para ofrecer "Actualizar regla existente" en vez de crear un duplicado.
   */
  existingRuleId?: string;
  existingCategoryId?: string;
  existingCategoryName?: string;
}
