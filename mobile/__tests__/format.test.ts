import {
  accountBalanceStatus,
  balanceStatusLabel,
  balanceStatusTone,
  balanceTypeLabel,
  formatCategoryShare,
  formatCompactCurrency,
  formatCurrency,
  formatDateInput,
  formatDayHeading,
  formatFullDateTime,
  formatMonthComparison,
  formatRelativeTime,
  groupByDay,
  initialsOf,
  maskLabel,
  parseAmountFilter,
  parseDateInput,
} from '../utils/format';

describe('formatCurrency', () => {
  it('always shows two decimals and thousands separators', () => {
    expect(formatCurrency(2846.2)).toBe('$2,846.20');
    expect(formatCurrency(1240.5)).toBe('$1,240.50');
    expect(formatCurrency(0)).toBe('$0.00');
    expect(formatCurrency(1234567.891)).toBe('$1,234,567.89');
  });

  it('places the minus sign before the symbol', () => {
    expect(formatCurrency(-48.2)).toBe('-$48.20');
  });

  it('can force an explicit sign for movement rows', () => {
    expect(formatCurrency(350, { signed: true })).toBe('+$350.00');
    expect(formatCurrency(-48.2, { signed: true })).toBe('-$48.20');
  });

  it('hides the amount when the user asked to', () => {
    expect(formatCurrency(2846.2, { hidden: true })).toBe('••••••');
  });
});

describe('formatCompactCurrency', () => {
  it('stays exact below ten thousand', () => {
    expect(formatCompactCurrency(1420)).toBe('$1,420.00');
  });

  it('compacts larger figures so tiles do not wrap', () => {
    expect(formatCompactCurrency(24500)).toBe('$24.5K');
    expect(formatCompactCurrency(-1_250_000)).toBe('-$1.3M');
  });
});

describe('formatDayHeading', () => {
  const today = new Date(2026, 2, 10, 12, 0, 0);

  it('uses relative wording for the days that matter', () => {
    expect(formatDayHeading(new Date(2026, 2, 10, 8, 0, 0), today)).toBe('HOY');
    expect(formatDayHeading(new Date(2026, 2, 9, 23, 0, 0), today)).toBe('AYER');
  });

  it('names the weekday inside the current week', () => {
    expect(formatDayHeading(new Date(2026, 2, 7, 10, 0, 0), today)).toBe('SÁBADO 7');
  });

  it('falls back to a date for anything older', () => {
    expect(formatDayHeading(new Date(2026, 1, 14, 10, 0, 0), today)).toBe('14 DE FEBRERO');
    expect(formatDayHeading(new Date(2025, 11, 24, 10, 0, 0), today)).toBe('24 DE DICIEMBRE 2025');
  });
});

describe('formatRelativeTime', () => {
  const now = new Date(2026, 2, 10, 12, 0, 0);

  it('describes recency the way the accounts screen needs it', () => {
    expect(formatRelativeTime(new Date(2026, 2, 10, 11, 55, 0).toISOString(), now)).toBe('hace 5 min');
    expect(formatRelativeTime(new Date(2026, 2, 10, 9, 0, 0).toISOString(), now)).toBe('hace 3 h');
    expect(formatRelativeTime(new Date(2026, 2, 7, 12, 0, 0).toISOString(), now)).toBe('hace 3 días');
    expect(formatRelativeTime(null, now)).toBe('nunca');
  });
});

describe('groupByDay', () => {
  const movements = [
    { id: '1', transactionDate: new Date(2026, 2, 10, 18, 0, 0).toISOString(), signedAmount: -48.2 },
    { id: '2', transactionDate: new Date(2026, 2, 10, 9, 0, 0).toISOString(), signedAmount: 350 },
    { id: '3', transactionDate: new Date(2026, 2, 9, 9, 0, 0).toISOString(), signedAmount: -12.99 },
  ];

  it('keeps the API order and totals each day', () => {
    const groups = groupByDay(movements, new Date(2026, 2, 10, 20, 0, 0));

    expect(groups).toHaveLength(2);
    expect(groups[0]?.heading).toBe('HOY');
    expect(groups[0]?.items.map((item) => item.id)).toEqual(['1', '2']);
    expect(groups[0]?.total).toBeCloseTo(301.8, 2);
    expect(groups[1]?.heading).toBe('AYER');
    expect(groups[1]?.total).toBeCloseTo(-12.99, 2);
  });

  it('returns nothing for an empty list', () => {
    expect(groupByDay([])).toEqual([]);
  });
});

describe('small helpers', () => {
  it('masks account numbers the way the design shows them', () => {
    expect(maskLabel('4821')).toBe('•••• 4821');
    expect(maskLabel(null)).toBe('');
  });

  it('never calls an estimated balance verified', () => {
    expect(balanceTypeLabel('Estimated')).toBe('Saldo estimado');
    expect(balanceTypeLabel('Verified')).toBe('Saldo verificado');
  });

  it('builds initials for the institution avatar', () => {
    expect(initialsOf('Banco Pichincha')).toBe('BP');
    expect(initialsOf('DEUNA')).toBe('D');
    expect(initialsOf('   ')).toBe('?');
  });

  it('formats a full timestamp for the movement detail', () => {
    expect(formatFullDateTime(new Date(2026, 2, 4, 18, 12, 0))).toBe('4 de marzo de 2026, 18:12');
  });
});

describe('accountBalanceStatus (Entregable 11)', () => {
  it('is verified only when the backend says so', () => {
    expect(accountBalanceStatus({ balanceType: 'Verified', lastVerifiedAt: null })).toBe('Verified');
  });

  it('is estimated when it drifted away from a real anchor', () => {
    expect(
      accountBalanceStatus({ balanceType: 'Estimated', lastVerifiedAt: new Date(2026, 2, 1).toISOString() }),
    ).toBe('Estimated');
  });

  it('needs an update when there has never been a real anchor at all', () => {
    expect(accountBalanceStatus({ balanceType: 'Estimated', lastVerifiedAt: null })).toBe('NeedsUpdate');
  });

  it('labels and colours each status distinctly', () => {
    expect(balanceStatusLabel('Verified')).toBe('Saldo verificado');
    expect(balanceStatusLabel('Estimated')).toBe('Saldo estimado');
    expect(balanceStatusLabel('NeedsUpdate')).toBe('Necesita actualización');

    expect(balanceStatusTone('Verified')).toBe('positive');
    expect(balanceStatusTone('Estimated')).toBe('neutral');
    expect(balanceStatusTone('NeedsUpdate')).toBe('attention');
  });
});

describe('formatDateInput / parseDateInput (Entregable 11)', () => {
  const now = new Date(2026, 2, 10, 12, 0, 0);

  it('round-trips a real date', () => {
    const text = formatDateInput(new Date(2026, 2, 4));
    expect(text).toBe('04/03/2026');
    expect(parseDateInput(text, now)).toEqual(new Date(2026, 2, 4));
  });

  it('rejects a date that does not exist rather than rolling it forward', () => {
    expect(parseDateInput('31/02/2026', now)).toBeNull();
  });

  it('rejects a balance the person could not have seen yet', () => {
    expect(parseDateInput('11/03/2026', now)).toBeNull();
    expect(parseDateInput('10/03/2026', now)).not.toBeNull();
  });

  it('rejects text that is not shaped like a date at all', () => {
    expect(parseDateInput('hoy', now)).toBeNull();
    expect(parseDateInput('', now)).toBeNull();
    expect(parseDateInput('4/3/26', now)).toBeNull();
  });
});

describe('parseAmountFilter (Entregable 12)', () => {
  it('parses a plain integer', () => {
    expect(parseAmountFilter('100')).toBe(100);
  });

  it('treats a comma as the decimal separator, same as the balance screen', () => {
    expect(parseAmountFilter('15,50')).toBe(15.5);
  });

  it('also accepts a period as the decimal separator', () => {
    expect(parseAmountFilter('15.50')).toBe(15.5);
  });

  it('trims surrounding whitespace', () => {
    expect(parseAmountFilter('  42  ')).toBe(42);
  });

  it('returns undefined for an empty filter so it is simply omitted', () => {
    expect(parseAmountFilter('')).toBeUndefined();
    expect(parseAmountFilter('   ')).toBeUndefined();
  });

  it('returns undefined for text that is not a number', () => {
    expect(parseAmountFilter('abc')).toBeUndefined();
  });

  it('rejects a negative amount -- a movement filter is never negative, the sign lives in direction', () => {
    expect(parseAmountFilter('-10')).toBeUndefined();
  });

  it('accepts zero as a valid lower bound', () => {
    expect(parseAmountFilter('0')).toBe(0);
  });
});

describe('formatMonthComparison (Entregable 15)', () => {
  it('returns null when there is nothing to compare against', () => {
    expect(formatMonthComparison(null)).toBeNull();
  });

  it('labels a spending increase as "más"', () => {
    expect(formatMonthComparison(12.4)).toEqual({ label: '12% más que el mes pasado', up: true });
  });

  it('labels a spending decrease as "menos"', () => {
    expect(formatMonthComparison(-8.2)).toEqual({ label: '8% menos que el mes pasado', up: false });
  });

  it('treats no change as "más" (>= 0), never a misleading "menos"', () => {
    expect(formatMonthComparison(0)).toEqual({ label: '0% más que el mes pasado', up: true });
  });

  it('rounds to the nearest whole percent', () => {
    expect(formatMonthComparison(12.6)).toEqual({ label: '13% más que el mes pasado', up: true });
  });
});

describe('formatCategoryShare', () => {
  it('shows one decimal so a dominant category does not round up to a misleading 100%', () => {
    expect(formatCategoryShare(99.8)).toBe('99.8%');
  });

  it('shows one decimal for a small but real category instead of rounding it to 0%', () => {
    expect(formatCategoryShare(0.2)).toBe('0.2%');
  });

  it('uses "<0.1%" for a nonzero share too small for one decimal to show', () => {
    expect(formatCategoryShare(0.05)).toBe('<0.1%');
  });

  it('shows a plain 0% only when the share truly is zero', () => {
    expect(formatCategoryShare(0)).toBe('0%');
  });
});
