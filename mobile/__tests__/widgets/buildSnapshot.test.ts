import { buildWidgetSnapshot } from '../../lib/widgets/buildSnapshot';
import type { Account, HomeSummary, RecurringPayment } from '../../types/api';

function account(overrides: Partial<Account> = {}): Account {
  return {
    id: 'acc-1',
    providerCode: 'PICHINCHA',
    providerName: 'Banco Pichincha',
    brandColor: '#FFD100',
    logoKey: 'pichincha',
    alias: 'Cuenta corriente',
    accountType: 'Checking',
    mask: '4821',
    currency: 'USD',
    connectionMode: 'ManualImport',
    balance: 500,
    balanceType: 'Verified',
    lastVerifiedBalance: 500,
    lastVerifiedAt: '2026-08-01T00:00:00Z',
    lastTransactionAt: '2026-08-30T00:00:00Z',
    lastSyncedAt: '2026-08-30T00:00:00Z',
    isArchived: false,
    isLiability: false,
    ...overrides,
  };
}

function summary(overrides: Partial<HomeSummary> = {}): HomeSummary {
  return {
    greeting: 'Hola',
    displayName: 'Ana',
    totalBalance: 1000,
    currency: 'USD',
    accountCount: 2,
    anyEstimatedBalance: false,
    month: { income: 800, expense: 300, net: 500 },
    monthComparison: { previousIncome: 700, previousExpense: 250, incomeChangePercent: 14, expenseChangePercent: 20 },
    accounts: [
      account(),
      account({ id: 'acc-2', alias: 'Tarjeta', accountType: 'CreditCard', isLiability: true, balance: 500, balanceType: 'Estimated' }),
    ],
    recentTransactions: [],
    categoryBreakdown: [
      { categoryId: 'cat-1', name: 'Comida', icon: 'fast-food', color: '#FF0000', total: 120, percentage: 40, count: 5 },
      { categoryId: 'cat-2', name: 'Transporte', icon: 'car', color: '#00FF00', total: 180, percentage: 60, count: 8 },
    ],
    staleAccounts: [],
    insights: [],
    ...overrides,
  };
}

function recurring(overrides: Partial<RecurringPayment> = {}): RecurringPayment {
  return {
    merchant: 'Netflix',
    categoryName: 'Entretenimiento',
    averageAmount: 15.99,
    occurrencesLast6Months: 5,
    lastSeenAt: '2026-08-15T00:00:00Z',
    ...overrides,
  };
}

const NOW = new Date('2026-08-20T12:00:00Z');

describe('buildWidgetSnapshot', () => {
  it('returns a logged-out snapshot when there is no session, regardless of any cached data', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: false,
      pulses: null,
      summary: summary(),
      recurringPayments: [recurring()],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.isAuthenticated).toBe(false);
    expect(snapshot.availableMoney.hasData).toBe(false);
    expect(snapshot.accounts).toHaveLength(0);
  });

  it('returns an authenticated-but-empty snapshot before the summary has loaded', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: null,
      recurringPayments: null,
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.isAuthenticated).toBe(true);
    expect(snapshot.totalBalance.hasData).toBe(false);
  });

  it('excludes credit card balances from "dinero disponible" but includes them in "saldo total"', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.availableMoney.amount).toBe(500); // only the Checking account
    expect(snapshot.totalBalance.amount).toBe(1000); // HomeSummary.totalBalance, unchanged
  });

  it('flags "dinero disponible" as estimated only when one of the liquid accounts is', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary({ accounts: [account({ balanceType: 'Estimated' })] }),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.availableMoney.isEstimated).toBe(true);
  });

  it('picks the most recently seen recurring payment for "próximo pago" and estimates a date 30 days after last seen', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [
        recurring({ merchant: 'Older', lastSeenAt: '2026-07-01T00:00:00Z' }),
        recurring({ merchant: 'Netflix', lastSeenAt: '2026-08-15T00:00:00Z' }),
      ],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.nextPayment.hasData).toBe(true);
    expect(snapshot.nextPayment.concept).toBe('Netflix');
    expect(snapshot.nextPayment.estimatedDate).toBe('2026-09-14T00:00:00.000Z');
  });

  it('has no data for "próximo pago" when there are no recurring payments, without crashing', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.nextPayment.hasData).toBe(false);
  });

  it('treats a null recurringPayments (analytics not loaded yet) the same as empty, not an error', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: null,
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.nextPayment.hasData).toBe(false);
  });

  it('sorts category spend by amount so the top category is first (the "Presupuesto" default)', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.categorySpend[0]?.categoryName).toBe('Transporte'); // 180 > 120
    expect(snapshot.categorySpend[1]?.categoryName).toBe('Comida');
  });

  it('carries the month expense comparison through unchanged', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.monthExpenses.amount).toBe(300);
    expect(snapshot.monthExpenses.previousAmount).toBe(250);
    expect(snapshot.monthExpenses.changePercent).toBe(20);
  });

  it('propagates amountsHidden through to the snapshot verbatim', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: true,
      now: NOW,
    });

    expect(snapshot.amountsHidden).toBe(true);
  });

  it('builds a matching deep link for every account and category widget entry', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary(),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.accounts.find((a) => a.accountId === 'acc-1')?.link?.uri).toBe(
      'fino:///movimientos?accountId=acc-1',
    );
    expect(snapshot.categorySpend.find((c) => c.categoryId === 'cat-1')?.link?.uri).toBe(
      'fino:///movimientos?categoryId=cat-1',
    );
  });

  it('never includes an archived account in the selectable/account lists', () => {
    const snapshot = buildWidgetSnapshot({
      isAuthenticated: true,
      pulses: null,
      summary: summary({ accounts: [account(), account({ id: 'acc-old', isArchived: true })] }),
      recurringPayments: [],
      amountsHidden: false,
      now: NOW,
    });

    expect(snapshot.accounts.some((a) => a.accountId === 'acc-old')).toBe(false);
    expect(snapshot.selectableAccounts.some((a) => a.id === 'acc-old')).toBe(false);
  });
});
