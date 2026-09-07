import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type TransactionQuery } from '../services/endpoints';
import type {
  AnalyticsPeriodCode,
  CreateCategorizationRuleRequest,
  NotificationPreferences,
  UpdateCategorizationRuleRequest,
} from '../types/api';

export const queryKeys = {
  summary: ['summary'] as const,
  accounts: ['accounts'] as const,
  account: (id: string) => ['accounts', id] as const,
  providers: ['providers'] as const,
  categories: ['categories'] as const,
  insights: ['insights'] as const,
  analyticsDashboard: (query: { period?: AnalyticsPeriodCode; from?: string; to?: string; accountId?: string }) =>
    ['analytics', 'dashboard', query] as const,
  transactions: (query: TransactionQuery) => ['transactions', query] as const,
  transaction: (id: string) => ['transactions', id] as const,
  emailConnections: ['email-connections'] as const,
  importHistory: ['imports', 'history'] as const,
  transferCandidates: ['transfers', 'candidates'] as const,
  notifications: ['notifications'] as const,
  sessions: ['auth', 'sessions'] as const,
  activity: ['auth', 'activity'] as const,
  // "Categorización personal": las reglas propias del usuario ("Reglas de
  // categorización", punto 15).
  categorizationRules: ['categorization-rules'] as const,
};

export function useSummary() {
  return useQuery({ queryKey: queryKeys.summary, queryFn: api.summary });
}

export function useAccounts() {
  return useQuery({ queryKey: queryKeys.accounts, queryFn: api.accounts.list });
}

export function useAccount(id: string | undefined) {
  return useQuery({
    queryKey: id ? queryKeys.account(id) : ['accounts', 'missing'],
    queryFn: () => api.accounts.get(id!),
    enabled: !!id,
  });
}

export function useProviders() {
  return useQuery({ queryKey: queryKeys.providers, queryFn: api.providers, staleTime: 5 * 60 * 1000 });
}

export function useCategories() {
  return useQuery({ queryKey: queryKeys.categories, queryFn: api.categories, staleTime: 5 * 60 * 1000 });
}

export function useInsights() {
  return useQuery({ queryKey: queryKeys.insights, queryFn: api.insights.list });
}

/** Entregable "Dashboard de estadísticas": KPIs, series, categorías y saldo histórico, todo en una sola llamada. */
export function useAnalyticsDashboard(query: { period?: AnalyticsPeriodCode; from?: string; to?: string; accountId?: string } = {}) {
  return useQuery({
    queryKey: queryKeys.analyticsDashboard(query),
    queryFn: () => api.analytics.dashboard(query),
  });
}

const PAGE_SIZE = 30;

/** Infinite scroll for the movements list. */
export function useTransactions(query: TransactionQuery) {
  return useInfiniteQuery({
    queryKey: queryKeys.transactions(query),
    initialPageParam: 1,
    queryFn: ({ pageParam }) => api.transactions.list({ ...query, page: pageParam, pageSize: PAGE_SIZE }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
  });
}

export function useTransaction(id: string) {
  return useQuery({ queryKey: queryKeys.transaction(id), queryFn: () => api.transactions.get(id), enabled: !!id });
}

/**
 * "Categorización personal" (punto 5/7): `createRule` es el toggle "Aplicar
 * también a movimientos similares" (true por defecto, como pedía el flujo
 * anterior) y `applyToExistingMatches` es el paso extra "este, anteriores y
 * futuros" -- solo se activa cuando la persona lo confirma explícitamente
 * después de ver el conteo de la vista previa (useRulePreview).
 */
export function useSetCategory(transactionId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({
      categoryId,
      createRule = true,
      applyToExistingMatches = false,
    }: {
      categoryId: string;
      createRule?: boolean;
      applyToExistingMatches?: boolean;
    }) => api.transactions.setCategory(transactionId, categoryId, createRule, applyToExistingMatches),
    onSuccess: () => {
      // A recategorisation changes the breakdown and the insights too; when
      // history was also recategorized, other movements' cards changed too.
      void client.invalidateQueries({ queryKey: queryKeys.transaction(transactionId) });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.categorizationRules });
    },
  });
}

/** Punto 6/19: "si creo esta regla, ¿qué movimientos coincidirían?", antes de guardar nada. */
export function useRulePreview() {
  return useMutation({
    mutationFn: (request: { transactionId: string; categoryId: string }) => api.categorizationRules.preview(request),
  });
}

/** Punto 15: "Reglas de categorización", accesible desde Ajustes. */
export function useCategorizationRules() {
  return useQuery({ queryKey: queryKeys.categorizationRules, queryFn: api.categorizationRules.list });
}

/** Punto 14: crear una regla directamente (fuera del flujo de corrección de un movimiento). */
export function useCreateCategorizationRule() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateCategorizationRuleRequest) => api.categorizationRules.create(request),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.categorizationRules });
    },
  });
}

/** Punto 15/16: activar/desactivar, cambiar categoría, y opcionalmente recategorizar el historial. */
export function useUpdateCategorizationRule() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, ...request }: { id: string } & UpdateCategorizationRuleRequest) =>
      api.categorizationRules.update(id, request),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.categorizationRules });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
    },
  });
}

/** Punto 17: borrar una regla nunca toca el historial ya categorizado -- solo deja de aplicarse a futuro. */
export function useDeleteCategorizationRule() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.categorizationRules.remove(id),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.categorizationRules });
    },
  });
}

export function useSetNote(transactionId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (note: string | null) => api.transactions.setNote(transactionId, note),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.transaction(transactionId) });
    },
  });
}

/**
 * Entregable 12: corrige el nombre de comercio que se muestra, sin tocar la
 * descripción original del banco ni la suposición automática. El movimiento
 * también aparece en la lista de movimientos, así que esa query se invalida
 * también (igual que useSetCategory).
 */
export function useSetMerchant(transactionId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (merchant: string | null) => api.transactions.setMerchant(transactionId, merchant),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.transaction(transactionId) });
      void client.invalidateQueries({ queryKey: ['transactions'] });
    },
  });
}

/**
 * Entregable 27: resuelve el aviso "lo guardamos aparte para que decidas tú"
 * de un movimiento NeedsReview. Cambia el estado y, por lo tanto, si cuenta
 * o no para el saldo -- por eso invalida resumen e insights igual que
 * useClearInternalTransfer, no solo el movimiento y la lista.
 */
export function useResolveDuplicate(transactionId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (keepAsSeparate: boolean) => api.transactions.resolveDuplicate(transactionId, keepAsSeparate),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.transaction(transactionId) });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.insights });
    },
  });
}

export function useCreateAccount() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: api.accounts.create,
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.accounts });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
    },
  });
}

/**
 * Entregable 11: the person's own confirmation of what they saw at the bank.
 * Refreshes the account (its badge flips to "verificado") and the home
 * summary, since the total shown there is built from account balances.
 */
export function useSetVerifiedBalance() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ accountId, balance, asOf }: { accountId: string; balance: number; asOf?: string }) =>
      api.accounts.setVerifiedBalance(accountId, balance, asOf),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.accounts });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
    },
  });
}

/** Entregable 13: candidate pairs shown as "¿Esto fue una transferencia entre tus cuentas?". */
export function useInternalTransferCandidates() {
  return useQuery({ queryKey: queryKeys.transferCandidates, queryFn: api.transfers.candidates });
}

export function useConfirmInternalTransfer() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ outgoingTransactionId, incomingTransactionId }: { outgoingTransactionId: string; incomingTransactionId: string }) =>
      api.transfers.confirm(outgoingTransactionId, incomingTransactionId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.transferCandidates });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.insights });
    },
  });
}

/** Undoes a confirmed transfer on both legs -- e.g. it was matched by mistake. */
export function useClearInternalTransfer() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (transactionId: string) => api.transfers.clear(transactionId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.transferCandidates });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.insights });
    },
  });
}

export function useEmailConnections() {
  return useQuery({ queryKey: queryKeys.emailConnections, queryFn: api.emailConnections.list });
}

/**
 * Privacy actions. Every one of them destroys data, so none is optimistic: the
 * screen waits for the server and then refetches, rather than showing a state
 * that might not be true.
 */
export function useDeleteTransactions() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (financialAccountId?: string) => api.privacy.deleteTransactions(financialAccountId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.accounts });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.insights });
    },
  });
}

export function useDeleteFinancialAccount() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (accountId: string) => api.privacy.deleteAccount(accountId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.accounts });
      void client.invalidateQueries({ queryKey: ['transactions'] });
    },
  });
}

export function useRevokeEmailConnection() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (connectionId: string) => api.emailConnections.revoke(connectionId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.emailConnections });
    },
  });
}

/**
 * Entregable 23 ("Preparar email ingestion, sin OAuth"): Gmail y Outlook siguen
 * detrás de un flujo de consentimiento real que todavía no existe (Entregables
 * 24/25) -- si el servidor no los tiene configurados, esta mutación falla con el
 * 409 que ya devuelve el backend y la pantalla lo muestra tal cual. Reenvío es la
 * única vía que puede terminar en éxito hoy.
 */
export function useStartEmailConnection() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ providerKind, emailAddress }: { providerKind: string; emailAddress: string }) =>
      api.emailConnections.start(providerKind, emailAddress),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.emailConnections });
    },
  });
}

export function useRefreshAfterImport() {
  const client = useQueryClient();

  return () => {
    void client.invalidateQueries({ queryKey: queryKeys.summary });
    void client.invalidateQueries({ queryKey: queryKeys.accounts });
    void client.invalidateQueries({ queryKey: ['transactions'] });
    void client.invalidateQueries({ queryKey: queryKeys.insights });
    void client.invalidateQueries({ queryKey: queryKeys.importHistory });
  };
}

/** Entregable 8: "Historial de importaciones" -- infinite scroll, newest first. */
export function useImportHistory() {
  return useInfiniteQuery({
    queryKey: queryKeys.importHistory,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => api.imports.list({ page: pageParam, pageSize: PAGE_SIZE }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
  });
}

export function useImportPreview(importId: string | undefined) {
  return useQuery({
    queryKey: importId ? ['imports', importId, 'preview'] : ['imports', 'missing', 'preview'],
    queryFn: () => api.imports.preview(importId!),
    enabled: !!importId,
  });
}

/**
 * Cancels a pending import (Received/PreviewReady). Used both from the
 * history screen's swipe-to-cancel and from the import wizard itself when the
 * user leaves the preview without confirming, so nothing is ever left
 * orphaned just because the screen was closed.
 */
export function useCancelImport() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (importId: string) => api.imports.cancel(importId),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.importHistory });
    },
  });
}

/** Entregable 17 ("Notificaciones"): últimas 50, más nueva primero. */
export function useNotifications() {
  return useQuery({ queryKey: queryKeys.notifications, queryFn: api.notifications.list });
}

export function useMarkNotificationRead() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.notifications.markRead(id),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.notifications });
    },
  });
}

/**
 * Updates the five preference categories (plus push/preview) for the current
 * device. There is no GET for a single device's preferences, so the caller
 * (the notifications screen, seeded from useDeviceStore) is what persists the
 * server's response back into that same store.
 */
export function useUpdateNotificationPreferences(expoPushToken: string | null) {
  return useMutation({
    mutationFn: (preferences: NotificationPreferences) => {
      if (!expoPushToken) {
        return Promise.reject(new Error('No hay un token de push registrado todavía.'));
      }
      return api.notifications.updatePreferences(expoPushToken, preferences);
    },
  });
}

/** Entregable 19 ("Sesiones y dispositivos"): dispositivos con sesión abierta. */
export function useSessions() {
  return useQuery({ queryKey: queryKeys.sessions, queryFn: api.auth.listSessions });
}

export function useRevokeSession() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => api.auth.revokeSession(id),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.sessions });
    },
  });
}

/** Entregable 21 ("Auditoría"): historial de actividad, scroll infinito. */
export function useSecurityActivity() {
  return useInfiniteQuery({
    queryKey: queryKeys.activity,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => api.auth.listActivity({ page: pageParam, pageSize: PAGE_SIZE }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
  });
}
