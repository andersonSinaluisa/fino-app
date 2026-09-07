/**
 * Home-screen widgets (iOS WidgetKit / Android App Widgets).
 *
 * A `WidgetSnapshot` is the ONLY thing that ever leaves the JS runtime and
 * reaches native widget code (via `ExtensionStorage` on iOS, via
 * `react-native-android-widget`'s renderer on Android). It is built once,
 * in `buildSnapshot.ts`, from data the app already fetches for its own
 * screens (`GET /summary`, `GET /analytics/dashboard`) -- widgets never
 * compute their own financial numbers, and nothing here is ever a mock:
 * every field traces back to a real account/transaction/category or is
 * explicitly marked as an estimate the same way the in-app "Proyección"
 * language already does.
 *
 * IMPORTANT (privacy): this snapshot is written to shared, sandboxed-but-
 * unencrypted storage (an App Group container on iOS, SharedPreferences on
 * Android) so the widget extension -- which runs as a *separate process*
 * without access to secure storage or the authenticated API client -- can
 * read it. Never add a token, refresh token, password, or any raw banking
 * credential to this type or to anything derived from it.
 */

export type WidgetKind =
  | 'available-money'
  | 'total-balance'
  | 'next-payment'
  | 'month-expenses'
  | 'category-spend'
  | 'projection'
  | 'account';

/** Every widget's tap target is a real, existing Fino screen -- see deepLinks.ts. */
export interface WidgetLink {
  /** e.g. "fino:///movimientos?categoryId=abc" -- always built from deepLinks.ts, never hand-typed. */
  uri: string;
}

interface WidgetBase {
  /** True once we have a real snapshot to show (as opposed to "sin datos"/"sesión cerrada"). */
  hasData: boolean;
  link: WidgetLink | null;
}

export interface AvailableMoneyWidgetData extends WidgetBase {
  kind: 'available-money';
  /** Sum of EstimatedBalance across non-archived, non-credit-card accounts -- "dinero que es tuyo para gastar". */
  amount: number;
  currency: string;
  /** True when at least one of the accounts summed here has an estimated (not bank-verified) balance. */
  isEstimated: boolean;
}

export interface TotalBalanceWidgetData extends WidgetBase {
  kind: 'total-balance';
  /** Reuses HomeSummary.totalBalance exactly -- every account, including credit cards. */
  amount: number;
  currency: string;
  accountCount: number;
  isEstimated: boolean;
}

export interface NextPaymentWidgetData extends WidgetBase {
  kind: 'next-payment';
  /** The recurring merchant Fino has seen most recently (AnalyticsDashboard.recurringPayments). */
  concept: string;
  categoryName: string | null;
  amount: number;
  currency: string;
  /**
   * Estimated next occurrence date -- Fino doesn't store a due date (there is
   * no bill/subscription schedule feature), so this is lastSeenAt + the
   * typical ~30-day gap already implied by "recurring" detection. Always
   * shown as an estimate in the UI, matching the "estimated" framing already
   * used for verified-vs-estimated balances.
   */
  estimatedDate: string;
  occurrences: number;
}

export interface MonthExpensesWidgetData extends WidgetBase {
  kind: 'month-expenses';
  amount: number;
  currency: string;
  previousAmount: number;
  changePercent: number | null;
}

/** "Presupuesto" redefined (per explicit product decision -- Fino has no budget/limit feature):
 * spend-in-category-this-month, no limit or progress bar. */
export interface CategorySpendWidgetData extends WidgetBase {
  kind: 'category-spend';
  categoryId: string | null;
  categoryName: string;
  categoryIcon: string | null;
  categoryColor: string | null;
  amount: number;
  currency: string;
  percentageOfMonth: number;
}

export interface ProjectionWidgetData extends WidgetBase {
  kind: 'projection';
  /** Current total balance, projected to month-end from month-to-date net income/expense. */
  projectedBalance: number;
  currentBalance: number;
  currency: string;
  daysRemaining: number;
}

/** "Cuenta" -- a single selectable account. */
export interface AccountWidgetData extends WidgetBase {
  kind: 'account';
  accountId: string | null;
  alias: string;
  providerName: string | null;
  brandColor: string | null;
  amount: number;
  currency: string;
  isEstimated: boolean;
}

export type AnyWidgetData =
  | AvailableMoneyWidgetData
  | TotalBalanceWidgetData
  | NextPaymentWidgetData
  | MonthExpensesWidgetData
  | CategorySpendWidgetData
  | ProjectionWidgetData
  | AccountWidgetData;

/**
 * Lightweight, widget-friendly copies of the pickable options (categories,
 * accounts) so a widget's native "select category/account" configuration UI
 * doesn't need its own network call.
 */
export interface SelectableCategory {
  id: string;
  name: string;
  icon: string | null;
  color: string | null;
}

export interface SelectableAccount {
  id: string;
  alias: string;
  providerName: string | null;
  brandColor: string | null;
}

/**
 * The full snapshot pushed to native storage. `generatedAt` lets a widget
 * decide a snapshot is too old to trust (e.g. show a subtle "actualizado
 * hace X" or fall back to "sin datos" past some threshold) without needing
 * its own clock synced to the server.
 */
export interface WidgetSnapshot {
  version: 1;
  generatedAt: string;
  /** False when there is no signed-in session -- every widget must render its "logged out" state. */
  isAuthenticated: boolean;
  /** Mirrors the in-app "Ocultar montos en widgets" preference; native code masks amounts when true. */
  amountsHidden: boolean;
  currency: string;
  availableMoney: AvailableMoneyWidgetData;
  totalBalance: TotalBalanceWidgetData;
  nextPayment: NextPaymentWidgetData;
  monthExpenses: MonthExpensesWidgetData;
  projection: ProjectionWidgetData;
  /** Every category with spend this month, for the "Presupuesto" widget's category picker + display. */
  categorySpend: CategorySpendWidgetData[];
  /** Every non-archived account, for the "Cuenta" widget's account picker + display. */
  accounts: AccountWidgetData[];
  selectableCategories: SelectableCategory[];
  selectableAccounts: SelectableAccount[];
}

/** The empty/logged-out snapshot -- what every widget shows before first sign-in or after logout. */
export function emptySnapshot(now: Date = new Date()): WidgetSnapshot {
  const noLink = null;
  return {
    version: 1,
    generatedAt: now.toISOString(),
    isAuthenticated: false,
    amountsHidden: false,
    currency: 'USD',
    availableMoney: { kind: 'available-money', hasData: false, link: noLink, amount: 0, currency: 'USD', isEstimated: false },
    totalBalance: { kind: 'total-balance', hasData: false, link: noLink, amount: 0, currency: 'USD', accountCount: 0, isEstimated: false },
    nextPayment: {
      kind: 'next-payment',
      hasData: false,
      link: noLink,
      concept: '',
      categoryName: null,
      amount: 0,
      currency: 'USD',
      estimatedDate: now.toISOString(),
      occurrences: 0,
    },
    monthExpenses: { kind: 'month-expenses', hasData: false, link: noLink, amount: 0, currency: 'USD', previousAmount: 0, changePercent: null },
    projection: { kind: 'projection', hasData: false, link: noLink, projectedBalance: 0, currentBalance: 0, currency: 'USD', daysRemaining: 0 },
    categorySpend: [],
    accounts: [],
    selectableCategories: [],
    selectableAccounts: [],
  };
}
