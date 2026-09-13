import type { RecurringPayment } from '../types/api';

export interface NextPaymentEstimate {
  payment: RecurringPayment;
  estimatedDate: Date;
}

const THIRTY_DAYS_MS = 30 * 24 * 60 * 60 * 1000;

/**
 * Fino's best guess at "next payment": the most recently seen recurring
 * payment (AnalyticsDashboard.recurringPayments), assumed to repeat ~30 days
 * after it was last seen -- there is no real due-date/subscription-schedule
 * feature, so this is always presented as an estimate.
 *
 * Shared by the OS home-screen widgets (lib/widgets/buildSnapshot.ts) and the
 * in-app Home screen's "Próximo" card, so both surfaces always agree instead
 * of growing two slightly different guesses at the same thing.
 */
export function pickNextPayment(
  payments: RecurringPayment[] | null | undefined,
  // Accepted for symmetry with other pure "now"-taking helpers (and so callers
  // can pass a fixed clock in tests) even though today's estimate is derived
  // purely from lastSeenAt, not from the current time.
  _now: Date = new Date(),
): NextPaymentEstimate | null {
  if (!payments || payments.length === 0) {
    return null;
  }

  const sorted = [...payments].sort(
    (a, b) => new Date(b.lastSeenAt).getTime() - new Date(a.lastSeenAt).getTime(),
  );
  const top = sorted[0]!;

  return {
    payment: top,
    estimatedDate: new Date(new Date(top.lastSeenAt).getTime() + THIRTY_DAYS_MS),
  };
}
