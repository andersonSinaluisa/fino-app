import type { HomeSummary } from '../types/api';

export interface AvailableMoneyEstimate {
  daysInMonth: number;
  dayOfMonth: number;
  daysRemaining: number;
  projectedMonthlyExpense: number;
  projectedRemainingExpense: number;
  availableUntilMonthEnd: number;
  recommendedDailySpend: number;
}

export function estimateAvailableMoney(summary: HomeSummary, today = new Date()): AvailableMoneyEstimate {
  const year = today.getFullYear();
  const month = today.getMonth();
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const dayOfMonth = Math.min(Math.max(today.getDate(), 1), daysInMonth);
  const daysRemaining = Math.max(daysInMonth - dayOfMonth, 0);
  const elapsedDays = Math.max(dayOfMonth, 1);

  const dailyExpensePace = summary.month.expense / elapsedDays;
  const projectedMonthlyExpense = dailyExpensePace * daysInMonth;
  const projectedRemainingExpense = Math.max(projectedMonthlyExpense - summary.month.expense, 0);
  const availableUntilMonthEnd = Math.max(summary.totalBalance - projectedRemainingExpense, 0);
  const recommendedDailySpend = daysRemaining > 0
    ? availableUntilMonthEnd / daysRemaining
    : availableUntilMonthEnd;

  return {
    daysInMonth,
    dayOfMonth,
    daysRemaining,
    projectedMonthlyExpense,
    projectedRemainingExpense,
    availableUntilMonthEnd,
    recommendedDailySpend,
  };
}

export function monthRangeLabel(today = new Date()): string {
  const month = today.toLocaleDateString('es-EC', { month: 'short' }).replace('.', '');
  const year = today.getFullYear();
  const daysInMonth = new Date(year, today.getMonth() + 1, 0).getDate();

  return `1 ${month} - ${daysInMonth} ${month}`;
}
