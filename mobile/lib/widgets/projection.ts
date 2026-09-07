/**
 * "Proyección" (widget #7): "saldo estimado a fin de mes". Fino's backend has
 * no forward-looking projection feature (its balance-history endpoint is
 * explicitly documented as "nunca una proyección hacia adelante" --
 * AnalyticsContracts.BalancePoint) so this is a deliberately simple, honest,
 * client-side estimate built only from real numbers the app already has:
 * today's total balance plus the month-to-date net (income - expense),
 * scaled up to a full month. No API is invented -- this is arithmetic over
 * data the summary endpoint already returns.
 */

export interface ProjectionInput {
  totalBalance: number;
  monthIncome: number;
  monthExpense: number;
  /** The day of the month "today" is, 1-based (e.g. 1st = 1). */
  dayOfMonth: number;
  /** Total days in the current month (28-31). */
  daysInMonth: number;
}

export interface ProjectionResult {
  projectedBalance: number;
  daysRemaining: number;
}

/**
 * Linear extrapolation: if the person's net cash flow so far this month
 * continues at the same daily rate for the rest of the month, this is the
 * balance they'd end up with. Deliberately does not try to be smarter than
 * that (no seasonality, no per-category modeling) -- a transparent, explainable
 * number beats a opaque "smart" one for a finance app.
 */
export function estimateMonthEndBalance(input: ProjectionInput): ProjectionResult {
  const { totalBalance, monthIncome, monthExpense, dayOfMonth, daysInMonth } = input;

  const safeDaysInMonth = Math.max(daysInMonth, 1);
  const clampedDay = Math.min(Math.max(dayOfMonth, 1), safeDaysInMonth);
  const daysRemaining = Math.max(safeDaysInMonth - clampedDay, 0);

  const netSoFar = monthIncome - monthExpense;
  // On day 1 there's nothing to extrapolate from yet -- avoid dividing by a
  // near-zero elapsed period blowing the estimate up; just carry the current
  // balance forward with no projected movement.
  const dailyRate = clampedDay > 0 ? netSoFar / clampedDay : 0;
  const projectedBalance = totalBalance + dailyRate * daysRemaining;

  return {
    projectedBalance: Math.round(projectedBalance * 100) / 100,
    daysRemaining,
  };
}
