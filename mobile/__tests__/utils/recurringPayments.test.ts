import { pickNextPayment } from '../../utils/recurringPayments';
import type { RecurringPayment } from '../../types/api';

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

describe('pickNextPayment', () => {
  it('returns null when there are no recurring payments', () => {
    expect(pickNextPayment([])).toBeNull();
  });

  it('returns null for null/undefined input', () => {
    expect(pickNextPayment(null)).toBeNull();
    expect(pickNextPayment(undefined)).toBeNull();
  });

  it('picks the most recently seen payment among several', () => {
    const result = pickNextPayment([
      recurring({ merchant: 'Older', lastSeenAt: '2026-07-01T00:00:00Z' }),
      recurring({ merchant: 'Netflix', lastSeenAt: '2026-08-15T00:00:00Z' }),
      recurring({ merchant: 'InBetween', lastSeenAt: '2026-08-01T00:00:00Z' }),
    ]);

    expect(result?.payment.merchant).toBe('Netflix');
  });

  it('estimates the next date as exactly 30 days after lastSeenAt', () => {
    const result = pickNextPayment([recurring({ lastSeenAt: '2026-08-15T00:00:00Z' })]);

    expect(result?.estimatedDate.toISOString()).toBe('2026-09-14T00:00:00.000Z');
  });

  it('does not mutate the input array order', () => {
    const payments = [
      recurring({ merchant: 'Older', lastSeenAt: '2026-07-01T00:00:00Z' }),
      recurring({ merchant: 'Netflix', lastSeenAt: '2026-08-15T00:00:00Z' }),
    ];
    pickNextPayment(payments);

    expect(payments[0]!.merchant).toBe('Older');
  });
});
