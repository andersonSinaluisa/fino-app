import { estimateAvailableMoney, monthRangeLabel } from '../utils/planCalculations';
import type { HomeSummary } from '../types/api';

function summary(overrides: Partial<HomeSummary> = {}): HomeSummary {
  return {
    greeting: 'Hola',
    displayName: 'Anderson',
    totalBalance: 1000,
    currency: 'USD',
    accountCount: 1,
    anyEstimatedBalance: false,
    month: { income: 0, expense: 150, net: -150 },
    monthComparison: {
      previousIncome: 0,
      previousExpense: 0,
      incomeChangePercent: null,
      expenseChangePercent: null,
    },
    accounts: [],
    recentTransactions: [],
    categoryBreakdown: [],
    staleAccounts: [],
    insights: [],
    ...overrides,
  };
}

describe('plan calculations', () => {
  it('projects remaining expenses from the current month pace', () => {
    const estimate = estimateAvailableMoney(summary(), new Date(2026, 8, 15));

    expect(estimate.daysInMonth).toBe(30);
    expect(estimate.dayOfMonth).toBe(15);
    expect(estimate.daysRemaining).toBe(15);
    expect(estimate.projectedMonthlyExpense).toBe(300);
    expect(estimate.projectedRemainingExpense).toBe(150);
    expect(estimate.availableUntilMonthEnd).toBe(850);
    expect(estimate.recommendedDailySpend).toBeCloseTo(56.67, 2);
  });

  it('never returns negative available money', () => {
    const estimate = estimateAvailableMoney(
      summary({ totalBalance: 50, month: { income: 0, expense: 300, net: -300 } }),
      new Date(2026, 8, 10),
    );

    expect(estimate.availableUntilMonthEnd).toBe(0);
    expect(estimate.recommendedDailySpend).toBe(0);
  });

  it('formats the current month range in Spanish', () => {
    expect(monthRangeLabel(new Date(2026, 8, 6))).toBe('1 sept - 30 sept');
  });
});
