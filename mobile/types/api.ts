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
}

export interface TransactionDetail extends Omit<TransactionListItem, 'brandColor' | 'categoryIcon' | 'categoryColor'> {
  providerName: string;
  accountMask: string | null;
  categoryManuallySet: boolean;
  externalReference: string | null;
  sourceConfidence: 'Low' | 'Medium' | 'High';
  note: string | null;
  possibleDuplicateOfId: string | null;
  importId: string | null;
  createdAt: string;
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
}

export interface HomeSummary {
  greeting: string;
  displayName: string;
  totalBalance: number;
  currency: string;
  accountCount: number;
  anyEstimatedBalance: boolean;
  month: { income: number; expense: number; net: number };
  accounts: Account[];
  recentTransactions: TransactionListItem[];
  categoryBreakdown: CategoryBreakdownItem[];
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
}

export interface ImportResult {
  importId: string;
  importedCount: number;
  skippedDuplicates: number;
  flaggedForReview: number;
  newEstimatedBalance: number;
  balanceType: BalanceType;
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

export interface NotificationPreferences {
  pushEnabled: boolean;
  showAmountsInPreview: boolean;
  notifyOnNewTransaction: boolean;
  notifyOnImportFinished: boolean;
  notifyOnWeeklySummary: boolean;
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}
