/**
 * Presentation helpers. Pure functions on purpose: they carry most of the
 * product's "voice" (how money and dates read in Spanish) and they are the
 * cheapest thing in the app to test.
 */

const MONTHS = [
  'enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio',
  'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre',
];

const WEEKDAYS = ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'];

export const HIDDEN_AMOUNT = '••••••';

/** "$1,240.50". Always two decimals: a finance app that drops cents looks broken. */
export function formatCurrency(amount: number, options: { hidden?: boolean; signed?: boolean } = {}): string {
  if (options.hidden) {
    return HIDDEN_AMOUNT;
  }

  const absolute = Math.abs(amount);
  const [whole, decimals] = absolute.toFixed(2).split('.');
  const grouped = (whole ?? '0').replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  const body = `$${grouped}.${decimals}`;

  if (!options.signed) {
    return amount < 0 ? `-${body}` : body;
  }

  return amount < 0 ? `-${body}` : `+${body}`;
}

/** Compact form for tiles: "$1.2K" once the number stops fitting. */
export function formatCompactCurrency(amount: number, hidden = false): string {
  if (hidden) {
    return HIDDEN_AMOUNT;
  }

  const absolute = Math.abs(amount);
  if (absolute < 10000) {
    return formatCurrency(amount);
  }

  const sign = amount < 0 ? '-' : '';
  if (absolute < 1_000_000) {
    return `${sign}$${(absolute / 1000).toFixed(1)}K`;
  }

  return `${sign}$${(absolute / 1_000_000).toFixed(1)}M`;
}

export function parseDate(value: string): Date {
  return new Date(value);
}

/** "HOY", "AYER", "LUNES 4" or "4 DE MARZO" — the grouping header in the movements list. */
export function formatDayHeading(value: string | Date, today = new Date()): string {
  const date = typeof value === 'string' ? parseDate(value) : value;

  const startOf = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const dayMs = 86_400_000;
  const diff = Math.round((startOf(today) - startOf(date)) / dayMs);

  if (diff === 0) return 'HOY';
  if (diff === 1) return 'AYER';

  if (diff > 1 && diff < 7) {
    return `${WEEKDAYS[date.getDay()] ?? ''} ${date.getDate()}`.toUpperCase();
  }

  if (date.getFullYear() === today.getFullYear()) {
    return `${date.getDate()} DE ${(MONTHS[date.getMonth()] ?? '').toUpperCase()}`;
  }

  return `${date.getDate()} DE ${(MONTHS[date.getMonth()] ?? '').toUpperCase()} ${date.getFullYear()}`;
}

/**
 * "Septiembre" (or "Septiembre 2025" if it isn't the current year) -- names the
 * calendar month the home screen's "Ingresaste"/"Gastaste"/"Balance del mes"
 * tiles are actually scoped to. Those figures reset to zero on the 1st of
 * every month regardless of transaction history, which reads as broken right
 * after a bulk import of older statements (all the income lands in a past
 * month, so "Ingresaste" legitimately shows $0 until new income is dated in
 * the current month) unless the tile says which month it means.
 */
export function currentMonthLabel(now = new Date()): string {
  const name = MONTHS[now.getMonth()] ?? '';
  const capitalized = name.charAt(0).toUpperCase() + name.slice(1);
  const currentYear = new Date().getFullYear();
  return now.getFullYear() === currentYear ? capitalized : `${capitalized} ${now.getFullYear()}`;
}

/** "4 de marzo de 2026, 18:12" for the movement detail. */
export function formatFullDateTime(value: string | Date): string {
  const date = typeof value === 'string' ? parseDate(value) : value;
  const hours = String(date.getHours()).padStart(2, '0');
  const minutes = String(date.getMinutes()).padStart(2, '0');

  return `${date.getDate()} de ${MONTHS[date.getMonth()]} de ${date.getFullYear()}, ${hours}:${minutes}`;
}

/** "04/03/2026" -- the format the "actualizar saldo" date field reads and writes. */
export function formatDateInput(date: Date): string {
  const day = String(date.getDate()).padStart(2, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  return `${day}/${month}/${date.getFullYear()}`;
}

/**
 * Parses a DD/MM/YYYY typed date, the same shape the app already reads from
 * bank exports. Returns null for anything that isn't a real calendar date
 * (rejects "31/02/2026" rather than silently rolling it into March) or that
 * falls after `now` -- a person cannot have seen tomorrow's balance yet.
 */
export function parseDateInput(value: string, now = new Date()): Date | null {
  const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(value.trim());
  if (!match) {
    return null;
  }

  const day = Number(match[1]);
  const month = Number(match[2]);
  const year = Number(match[3]);
  const date = new Date(year, month - 1, day);

  const isRealDate = date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
  if (!isRealDate) {
    return null;
  }

  return date.getTime() > now.getTime() ? null : date;
}

/** "hace 5 min", "hace 2 h", "hace 3 días", "nunca". */
export function formatRelativeTime(value: string | null | undefined, now = new Date()): string {
  if (!value) {
    return 'nunca';
  }

  const date = parseDate(value);
  const seconds = Math.max(0, Math.floor((now.getTime() - date.getTime()) / 1000));

  if (seconds < 60) return 'hace un momento';
  if (seconds < 3600) return `hace ${Math.floor(seconds / 60)} min`;
  if (seconds < 86400) return `hace ${Math.floor(seconds / 3600)} h`;

  const days = Math.floor(seconds / 86400);
  if (days === 1) return 'hace 1 día';
  if (days < 30) return `hace ${days} días`;

  const months = Math.floor(days / 30);
  return months === 1 ? 'hace 1 mes' : `hace ${months} meses`;
}

export interface DayGroup<T> {
  key: string;
  heading: string;
  items: T[];
  total: number;
}

/**
 * Groups movements by calendar day, preserving the incoming order (the API
 * already returns newest first). Returns the day total too, which is what makes
 * the list feel like a statement rather than a feed.
 */
export function groupByDay<T extends { transactionDate: string; signedAmount: number }>(
  items: T[],
  today = new Date(),
): DayGroup<T>[] {
  const groups = new Map<string, DayGroup<T>>();

  for (const item of items) {
    const date = parseDate(item.transactionDate);
    const key = `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;

    const existing = groups.get(key);
    if (existing) {
      existing.items.push(item);
      existing.total += item.signedAmount;
    } else {
      groups.set(key, {
        key,
        heading: formatDayHeading(date, today),
        items: [item],
        total: item.signedAmount,
      });
    }
  }

  return Array.from(groups.values());
}

export function maskLabel(mask: string | null | undefined): string {
  return mask ? `•••• ${mask}` : '';
}

export function balanceTypeLabel(balanceType: 'Verified' | 'Estimated'): string {
  return balanceType === 'Verified' ? 'Saldo verificado' : 'Saldo estimado';
}

export type AccountBalanceStatus = 'Verified' | 'Estimated' | 'NeedsUpdate';

/**
 * Entregable 11: a third status alongside "verificado"/"estimado" for an
 * account that has never had a real verified balance -- its whole estimate
 * comes from imported movements alone, with no anchor the person actually
 * saw at their bank. That is a stronger form of uncertainty than "verified
 * once, drifted since", so it earns its own label and its own call to
 * action instead of being folded into "Estimado".
 */
export function accountBalanceStatus(account: {
  balanceType: 'Verified' | 'Estimated';
  lastVerifiedAt: string | null;
}): AccountBalanceStatus {
  if (account.balanceType === 'Verified') {
    return 'Verified';
  }

  return account.lastVerifiedAt === null ? 'NeedsUpdate' : 'Estimated';
}

export function balanceStatusLabel(status: AccountBalanceStatus): string {
  switch (status) {
    case 'Verified':
      return 'Saldo verificado';
    case 'Estimated':
      return 'Saldo estimado';
    case 'NeedsUpdate':
      return 'Necesita actualización';
    default:
      return status satisfies never;
  }
}

export function balanceStatusTone(status: AccountBalanceStatus): 'positive' | 'neutral' | 'attention' {
  switch (status) {
    case 'Verified':
      return 'positive';
    case 'Estimated':
      return 'neutral';
    case 'NeedsUpdate':
      return 'attention';
    default:
      return status satisfies never;
  }
}

/**
 * Entregable 12: parses a "monto mínimo/máximo" filter typed on the movements
 * screen. Same convention as the balance field in actualizar-saldo.tsx -- a
 * comma is treated as the decimal separator. Empty or non-numeric text ->
 * undefined (the filter is simply omitted); negative amounts are rejected
 * because a movement's magnitude is never negative -- the sign lives in
 * Direction, not in the amount filter.
 */
export function parseAmountFilter(text: string): number | undefined {
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return undefined;
  }

  const value = Number(trimmed.replace(',', '.'));
  return Number.isFinite(value) && value >= 0 ? value : undefined;
}

/**
 * Entregable 15 ("Dashboard final MVP"): "12% más" / "8% menos" for the
 * month-over-month expense comparison. `null` means there is nothing from the
 * previous month to compare against (division by zero would be meaningless),
 * so the caller should omit the line entirely rather than show a fake 0%.
 */
export function formatMonthComparison(percentChange: number | null): { label: string; up: boolean } | null {
  if (percentChange === null || !Number.isFinite(percentChange)) {
    return null;
  }

  const rounded = Math.round(Math.abs(percentChange));
  return {
    label: `${rounded}% ${percentChange >= 0 ? 'más' : 'menos'} que el mes pasado`,
    up: percentChange >= 0,
  };
}

/**
 * Dashboard de estadísticas: igual que `formatMonthComparison`, pero para
 * cualquier periodo (no solo "el mes pasado") -- el dashboard compara contra
 * una ventana anterior de la misma duración, sea "este mes", "últimos 3
 * meses" o un rango personalizado, así que el texto no puede fijar "mes".
 */
export function formatPeriodChange(
  percentChange: number | null,
  referenceLabel = 'el periodo anterior',
): { label: string; up: boolean } | null {
  if (percentChange === null || !Number.isFinite(percentChange)) {
    return null;
  }

  const rounded = Math.round(Math.abs(percentChange));
  return {
    label: `${rounded}% ${percentChange >= 0 ? 'más' : 'menos'} que ${referenceLabel}`,
    up: percentChange >= 0,
  };
}

/** "6 sep" -- fecha corta absoluta, usada en rankings donde una etiqueta relativa (HOY/AYER) no tendría sentido. */
export function formatShortDate(value: string | Date): string {
  const date = typeof value === 'string' ? parseDate(value) : value;
  return `${date.getDate()} ${(MONTHS[date.getMonth()] ?? '').slice(0, 3)}`;
}

export function initialsOf(value: string): string {
  const words = value.trim().split(/\s+/).slice(0, 2);
  return words.map((w) => w.charAt(0).toUpperCase()).join('') || '?';
}
