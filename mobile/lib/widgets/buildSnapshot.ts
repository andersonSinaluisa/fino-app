import type { Account, HomeSummary, RecurringPayment } from '../../types/api';
import { widgetDeepLinks } from './deepLinks';
import { estimateMonthEndBalance } from './projection';
import {
  emptySnapshot,
  type AccountWidgetData,
  type CategorySpendWidgetData,
  type WidgetSnapshot,
} from './types';

const THIRTY_DAYS_MS = 30 * 24 * 60 * 60 * 1000;

export interface BuildSnapshotInput {
  isAuthenticated: boolean;
  /** null while the summary hasn't loaded yet (or the request failed) -- renders as "sin datos", never a mock. */
  summary: HomeSummary | null;
  /** null while the analytics dashboard hasn't loaded yet -- only "Próximo pago" depends on this. */
  recurringPayments: RecurringPayment[] | null;
  amountsHidden: boolean;
  now?: Date;
}

function isLiquidAccount(account: Account): boolean {
  // "Dinero disponible" = money that's actually the person's to spend, so a
  // credit card's balance (what they OWE the bank) is excluded -- it's a
  // liability, not spendable cash. Every other account type (Checking,
  // Savings, Wallet, Other) counts. This is a real, derived aggregation over
  // fields the backend already sends (AccountDto.accountType/balance), not a
  // new concept invented client-side.
  return account.accountType !== 'CreditCard' && !account.isArchived;
}

function daysInMonth(year: number, monthIndexZeroBased: number): number {
  return new Date(year, monthIndexZeroBased + 1, 0).getDate();
}

function mapAccount(account: Account): AccountWidgetData {
  return {
    kind: 'account',
    hasData: true,
    link: { uri: widgetDeepLinks.movementsByAccount(account.id) },
    accountId: account.id,
    alias: account.alias,
    providerName: account.providerName,
    brandColor: account.brandColor,
    amount: account.balance,
    currency: account.currency,
    isEstimated: account.balanceType === 'Estimated',
  };
}

function mapCategorySpend(item: HomeSummary['categoryBreakdown'][number], currency: string): CategorySpendWidgetData {
  return {
    kind: 'category-spend',
    hasData: true,
    link: { uri: widgetDeepLinks.movementsByCategory(item.categoryId) },
    categoryId: item.categoryId,
    categoryName: item.name,
    categoryIcon: item.icon,
    categoryColor: item.color,
    amount: item.total,
    currency,
    percentageOfMonth: item.percentage,
  };
}

/**
 * Pure function: HomeSummary + AnalyticsDashboard.recurringPayments (both
 * already-real data the app fetches for its own screens) in, a single
 * WidgetSnapshot out. No I/O, no platform APIs -- this is what native sync
 * code calls right before writing to ExtensionStorage/Android shared
 * storage, and it's also what makes the widget logic unit-testable without
 * a device or simulator.
 */
export function buildWidgetSnapshot(input: BuildSnapshotInput): WidgetSnapshot {
  const now = input.now ?? new Date();
  const base = emptySnapshot(now);

  if (!input.isAuthenticated) {
    return { ...base, isAuthenticated: false, amountsHidden: input.amountsHidden };
  }

  if (!input.summary) {
    // Signed in, but nothing fetched yet (cold start, offline first launch).
    return { ...base, isAuthenticated: true, amountsHidden: input.amountsHidden };
  }

  const { summary } = input;
  const currency = summary.currency;

  const liquidAccounts = summary.accounts.filter(isLiquidAccount);
  const availableAmount = round2(liquidAccounts.reduce((sum, a) => sum + a.balance, 0));
  const availableIsEstimated = liquidAccounts.some((a) => a.balanceType === 'Estimated');

  const projection = estimateMonthEndBalance({
    totalBalance: summary.totalBalance,
    monthIncome: summary.month.income,
    monthExpense: summary.month.expense,
    dayOfMonth: now.getDate(),
    daysInMonth: daysInMonth(now.getFullYear(), now.getMonth()),
  });

  const sortedRecurring = [...(input.recurringPayments ?? [])].sort(
    (a, b) => new Date(b.lastSeenAt).getTime() - new Date(a.lastSeenAt).getTime(),
  );
  const topRecurring = sortedRecurring[0] ?? null;

  // Sorted by amount so index 0 is always "top category this month" -- the
  // default the "Presupuesto" widget shows before native per-instance
  // configuration (see WIDGETS.md) picks something else.
  const categorySpend = [...summary.categoryBreakdown]
    .sort((a, b) => b.total - a.total)
    .map((item) => mapCategorySpend(item, currency));
  // Accounts already arrive ordered by the backend's own DisplayOrder/alias
  // sort (TransactionService.GetHomeSummaryAsync), so index 0 is a stable,
  // meaningful default for the "Cuenta" widget.
  const accounts = summary.accounts.filter((a) => !a.isArchived).map(mapAccount);

  const snapshot: WidgetSnapshot = {
    ...base,
    isAuthenticated: true,
    amountsHidden: input.amountsHidden,
    currency,
    availableMoney: {
      kind: 'available-money',
      hasData: liquidAccounts.length > 0,
      link: { uri: widgetDeepLinks.home() },
      amount: availableAmount,
      currency,
      isEstimated: availableIsEstimated,
    },
    totalBalance: {
      kind: 'total-balance',
      hasData: summary.accountCount > 0,
      link: { uri: widgetDeepLinks.accounts() },
      amount: summary.totalBalance,
      currency,
      accountCount: summary.accountCount,
      isEstimated: summary.anyEstimatedBalance,
    },
    nextPayment: topRecurring
      ? {
          kind: 'next-payment',
          hasData: true,
          link: { uri: widgetDeepLinks.statistics() },
          concept: topRecurring.merchant,
          categoryName: topRecurring.categoryName,
          amount: topRecurring.averageAmount,
          currency,
          estimatedDate: new Date(new Date(topRecurring.lastSeenAt).getTime() + THIRTY_DAYS_MS).toISOString(),
          occurrences: topRecurring.occurrencesLast6Months,
        }
      : { ...base.nextPayment, hasData: false, link: { uri: widgetDeepLinks.statistics() } },
    monthExpenses: {
      kind: 'month-expenses',
      hasData: true,
      link: { uri: widgetDeepLinks.statistics() },
      amount: summary.month.expense,
      currency,
      previousAmount: summary.monthComparison.previousExpense,
      changePercent: summary.monthComparison.expenseChangePercent,
    },
    projection: {
      kind: 'projection',
      hasData: true,
      link: { uri: widgetDeepLinks.statistics() },
      projectedBalance: projection.projectedBalance,
      currentBalance: summary.totalBalance,
      currency,
      daysRemaining: projection.daysRemaining,
    },
    categorySpend,
    accounts,
    selectableCategories: categorySpend.map((c) => ({
      id: c.categoryId ?? '',
      name: c.categoryName,
      icon: c.categoryIcon,
      color: c.categoryColor,
    })),
    selectableAccounts: accounts.map((a) => ({
      id: a.accountId ?? '',
      alias: a.alias,
      providerName: a.providerName,
      brandColor: a.brandColor,
    })),
  };

  return snapshot;
}

function round2(value: number): number {
  return Math.round(value * 100) / 100;
}
