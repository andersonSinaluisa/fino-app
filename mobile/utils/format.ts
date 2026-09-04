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

/** "4 de marzo de 2026, 18:12" for the movement detail. */
export function formatFullDateTime(value: string | Date): string {
  const date = typeof value === 'string' ? parseDate(value) : value;
  const hours = String(date.getHours()).padStart(2, '0');
  const minutes = String(date.getMinutes()).padStart(2, '0');

  return `${date.getDate()} de ${MONTHS[date.getMonth()]} de ${date.getFullYear()}, ${hours}:${minutes}`;
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

export function initialsOf(value: string): string {
  const words = value.trim().split(/\s+/).slice(0, 2);
  return words.map((w) => w.charAt(0).toUpperCase()).join('') || '?';
}
