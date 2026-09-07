import { estimateMonthEndBalance } from '../../lib/widgets/projection';

describe('estimateMonthEndBalance', () => {
  it('extrapolates the month-to-date net rate to the rest of the month', () => {
    // 15 days in, net +$300 so far ($20/day) -> 15 days left at that same
    // rate adds another $300.
    const result = estimateMonthEndBalance({
      totalBalance: 1000,
      monthIncome: 500,
      monthExpense: 200,
      dayOfMonth: 15,
      daysInMonth: 30,
    });

    expect(result.daysRemaining).toBe(15);
    expect(result.projectedBalance).toBe(1300);
  });

  it('projects a loss forward when spending exceeds income so far', () => {
    const result = estimateMonthEndBalance({
      totalBalance: 1000,
      monthIncome: 0,
      monthExpense: 300,
      dayOfMonth: 10,
      daysInMonth: 30,
    });

    // -$30/day * 20 remaining days = -$600
    expect(result.projectedBalance).toBe(400);
  });

  it('does not divide by zero on the first day of the month', () => {
    const result = estimateMonthEndBalance({
      totalBalance: 1000,
      monthIncome: 50,
      monthExpense: 0,
      dayOfMonth: 1,
      daysInMonth: 31,
    });

    expect(Number.isFinite(result.projectedBalance)).toBe(true);
    expect(result.daysRemaining).toBe(30);
  });

  it('clamps a day-of-month past the month length instead of going negative', () => {
    const result = estimateMonthEndBalance({
      totalBalance: 500,
      monthIncome: 0,
      monthExpense: 0,
      dayOfMonth: 45,
      daysInMonth: 30,
    });

    expect(result.daysRemaining).toBe(0);
    expect(result.projectedBalance).toBe(500);
  });
});
