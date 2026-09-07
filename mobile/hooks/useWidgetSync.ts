import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { buildWidgetSnapshot } from '../lib/widgets/buildSnapshot';
import { pushWidgetSnapshot } from '../lib/widgets/sync';
import { api } from '../services/endpoints';
import { useAuthStore } from '../store/authStore';
import { usePreferencesStore } from '../store/preferencesStore';
import { queryKeys } from './queries';

/**
 * Keeps the home-screen widgets (iOS/Android) in sync with the app's real
 * data, without any widget or mutation duplicating the financial logic that
 * already lives in `buildWidgetSnapshot`. Mounted once, in app/_layout.tsx,
 * for the whole lifetime of the app (unlike `useSummary`/`useAnalyticsDashboard`,
 * which only run while their screen is mounted) -- so it needs its own
 * `enabled: isAuthenticated` guard rather than reusing those hooks directly,
 * to avoid calling authenticated endpoints while signed out. It shares their
 * exact query keys (`queryKeys.summary`, `queryKeys.analyticsDashboard`), so
 * TanStack Query still dedupes with whatever the home/estadísticas screens
 * already fetched -- this never doubles a network request.
 *
 * Every existing mutation that changes movements, accounts, categories, or
 * transfers already calls `client.invalidateQueries({ queryKey: queryKeys.summary })`
 * (see hooks/queries.ts), so this effect re-runs and re-syncs automatically
 * whenever any of that happens; no mutation needed to be touched to satisfy
 * "actualizar los widgets cuando cambien movimientos/cuentas/etc."
 */
export function useWidgetSync(): void {
  const status = useAuthStore((state) => state.status);
  const hideAmountsInWidgets = usePreferencesStore((state) => state.hideAmountsInWidgets);
  const isAuthenticated = status === 'authenticated';

  const summaryQuery = useQuery({
    queryKey: queryKeys.summary,
    queryFn: api.summary,
    enabled: isAuthenticated,
  });

  const analyticsQuery = useQuery({
    queryKey: queryKeys.analyticsDashboard({}),
    queryFn: () => api.analytics.dashboard({}),
    enabled: isAuthenticated,
    // "Próximo pago" only needs the recurring-payments list, which the
    // backend always computes over a fixed 6-month window regardless of
    // period (AnalyticsService.GetDashboardAsync) -- a longer staleTime than
    // the app-wide default keeps this background fetch infrequent.
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (status === 'loading') {
      // Session restore hasn't resolved yet -- avoid a spurious "logged out" push.
      return;
    }

    const snapshot = buildWidgetSnapshot({
      isAuthenticated,
      summary: isAuthenticated ? (summaryQuery.data ?? null) : null,
      recurringPayments: isAuthenticated ? (analyticsQuery.data?.recurringPayments ?? null) : null,
      amountsHidden: hideAmountsInWidgets,
    });

    pushWidgetSnapshot(snapshot);
  }, [status, isAuthenticated, summaryQuery.data, analyticsQuery.data, hideAmountsInWidgets]);
}
