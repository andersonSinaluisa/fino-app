import { AnalyticsEvent, BudgetPeriodValue, track } from '../../services/analytics';
import type { BudgetPeriod } from '../../types/api';

/**
 * `budget_exceeded` se emite una vez por presupuesto y período en la sesión,
 * no cada vez que la lista se vuelve a pintar. La clave (id + inicio del
 * período) vive solo en memoria y NUNCA se manda: el evento lleva únicamente
 * el tipo de período.
 */
const reported = new Set<string>();

const PERIOD: Record<BudgetPeriod, string> = {
  Weekly: BudgetPeriodValue.Weekly,
  Biweekly: BudgetPeriodValue.Biweekly,
  Monthly: BudgetPeriodValue.Monthly,
  Custom: BudgetPeriodValue.Custom,
};

export function reportExceededOnce(budgetId: string, windowStart: string, period: BudgetPeriod): void {
  const key = `${budgetId}:${windowStart}`;
  if (reported.has(key)) {
    return;
  }
  reported.add(key);
  track(AnalyticsEvent.BudgetExceeded, { period: PERIOD[period] });
}

/** Solo para tests. */
export function resetExceededTracking(): void {
  reported.clear();
}
