import { request, upload } from './apiClient';
import type {
  Account,
  AnalyticsDashboard,
  AnalyticsPeriodCode,
  AppNotification,
  AuthResult,
  Category,
  CategorizationRule,
  CreateCategorizationRuleRequest,
  EmailConnection,
  HomeSummary,
  ImportPreview,
  ImportResult,
  ImportSummary,
  Insight,
  InternalTransferCandidate,
  ManualColumnMapping,
  NotificationPreferences,
  Paged,
  Provider,
  RulePreview,
  RulePreviewRequest,
  SecurityEvent,
  Session,
  TransactionDetail,
  TransactionListItem,
  UpdateCategorizationRuleRequest,
} from '../types/api';

export interface TransactionQuery {
  search?: string;
  accountId?: string;
  categoryId?: string;
  direction?: 'Income' | 'Expense';
  from?: string;
  to?: string;
  /** Entregable 12: institución (p.ej. "PICHINCHA"), útil con varias cuentas del mismo banco. */
  providerCode?: string;
  /** Entregable 12: límites sobre la magnitud del movimiento, nunca sobre el valor con signo. */
  minAmount?: number;
  maxAmount?: number;
  page?: number;
  pageSize?: number;
}

function toQueryString(query: Readonly<Record<string, string | number | undefined>>): string {
  const entries = Object.entries(query).filter(
    ([, value]) => value !== undefined && value !== '' && value !== null,
  );

  if (entries.length === 0) {
    return '';
  }

  const params = entries.map(
    ([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`,
  );

  return `?${params.join('&')}`;
}

export const api = {
  auth: {
    register: (email: string, password: string, displayName: string) =>
      request<AuthResult>('/api/v1/auth/register', {
        method: 'POST',
        authenticated: false,
        body: { email, password, displayName },
      }),

    login: (email: string, password: string) =>
      request<AuthResult>('/api/v1/auth/login', {
        method: 'POST',
        authenticated: false,
        body: { email, password },
      }),

    refresh: (refreshToken: string) =>
      request<AuthResult>('/api/v1/auth/refresh', {
        method: 'POST',
        authenticated: false,
        body: { refreshToken },
      }),

    logout: (refreshToken: string) =>
      request<void>('/api/v1/auth/logout', { method: 'POST', body: { refreshToken } }),

    /** Entregable 19: cierra la sesión en todos los dispositivos, incluido este. */
    logoutAll: () => request<void>('/api/v1/auth/logout-all', { method: 'POST' }),

    listSessions: () => request<Session[]>('/api/v1/auth/sessions'),

    /** Rejected with 409 for the caller's own session -- use logout() for that one. */
    revokeSession: (id: string) => request<void>(`/api/v1/auth/sessions/${id}`, { method: 'DELETE' }),

    /** Entregable 21 ("Auditoría"): historial de actividad de la cuenta, más reciente primero. */
    listActivity: (query: { page?: number; pageSize?: number } = {}) =>
      request<Paged<SecurityEvent>>(`/api/v1/auth/activity${toQueryString({ ...query })}`),
  },

  summary: () => request<HomeSummary>('/api/v1/summary'),

  providers: () => request<Provider[]>('/api/v1/providers'),

  accounts: {
    list: () => request<Account[]>('/api/v1/accounts'),

    get: (id: string) => request<Account>(`/api/v1/accounts/${id}`),

    create: (payload: {
      providerCode: string;
      alias: string;
      accountType: string;
      connectionMode: string;
      mask?: string | null;
      openingVerifiedBalance?: number | null;
      currency?: string;
    }) => request<Account>('/api/v1/accounts', { method: 'POST', body: payload }),

    // Entregable 11: asOf defaults to now, but the person can say the balance
    // they saw was as of an earlier date (e.g. their last statement date).
    setVerifiedBalance: (id: string, balance: number, asOf?: string) =>
      request<Account>(`/api/v1/accounts/${id}/verified-balance`, {
        method: 'POST',
        body: { balance, asOf: asOf ?? new Date().toISOString() },
      }),

    archive: (id: string) => request<void>(`/api/v1/accounts/${id}`, { method: 'DELETE' }),
  },

  transactions: {
    list: (query: TransactionQuery = {}) =>
      request<Paged<TransactionListItem>>(`/api/v1/transactions${toQueryString({ ...query })}`),

    get: (id: string) => request<TransactionDetail>(`/api/v1/transactions/${id}`),

    // "Categorización personal": createRule refleja el toggle "Aplicar
    // también a movimientos similares" (punto 5); applyToExistingMatches es
    // el paso extra "este, anteriores y futuros" (punto 7) -- nunca se activa
    // sin que la persona lo pida explícitamente.
    setCategory: (id: string, categoryId: string, createRule = true, applyToExistingMatches = false) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/category`, {
        method: 'PUT',
        body: { categoryId, createRule, applyToExistingMatches },
      }),

    setNote: (id: string, note: string | null) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/note`, {
        method: 'PUT',
        body: { note },
      }),

    // Entregable 12: null o vacío borra la corrección y vuelve a la
    // suposición automática -- nunca toca la descripción original del banco.
    setMerchant: (id: string, merchant: string | null) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/merchant`, {
        method: 'PUT',
        body: { merchant },
      }),

    // Entregable 27: la persona decide si un "posible duplicado" es un
    // movimiento propio (keepAsSeparate = true, vuelve a Confirmado) o el
    // mismo que ya tenía (keepAsSeparate = false, se ignora -- nunca se borra).
    resolveDuplicate: (id: string, keepAsSeparate: boolean) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/duplicate-review`, {
        method: 'PUT',
        body: { keepAsSeparate },
      }),
  },

  // Entregable 13: "¿Esto fue una transferencia entre tus cuentas?"
  transfers: {
    candidates: () => request<InternalTransferCandidate[]>('/api/v1/transfers/candidates'),

    confirm: (outgoingTransactionId: string, incomingTransactionId: string) =>
      request<void>('/api/v1/transfers/confirm', {
        method: 'POST',
        body: { outgoingTransactionId, incomingTransactionId },
      }),

    clear: (transactionId: string) =>
      request<void>(`/api/v1/transfers/${transactionId}/clear`, { method: 'POST' }),
  },

  categories: () => request<Category[]>('/api/v1/categories'),

  // "Categorización personal": administración directa de las reglas propias
  // del usuario (pantalla "Reglas de categorización", punto 15) y la
  // vista previa de impacto que usa tanto esa pantalla como el flujo de
  // "aplicar también a movimientos similares" (punto 6/19).
  categorizationRules: {
    list: () => request<CategorizationRule[]>('/api/v1/categorization-rules'),

    create: (body: CreateCategorizationRuleRequest) =>
      request<CategorizationRule>('/api/v1/categorization-rules', { method: 'POST', body }),

    update: (id: string, body: UpdateCategorizationRuleRequest) =>
      request<CategorizationRule>(`/api/v1/categorization-rules/${id}`, { method: 'PUT', body }),

    remove: (id: string) => request<void>(`/api/v1/categorization-rules/${id}`, { method: 'DELETE' }),

    preview: (body: RulePreviewRequest) =>
      request<RulePreview>('/api/v1/categorization-rules/preview', { method: 'POST', body }),
  },

  analytics: {
    dashboard: (query: { period?: AnalyticsPeriodCode; from?: string; to?: string; accountId?: string } = {}) =>
      request<AnalyticsDashboard>(`/api/v1/analytics/dashboard${toQueryString({ ...query })}`),
  },

  insights: {
    list: () => request<Insight[]>('/api/v1/insights'),
    refresh: () => request<Insight[]>('/api/v1/insights/refresh', { method: 'POST' }),
  },

  imports: {
    upload: (accountId: string, file: { uri: string; name: string; mimeType: string; size?: number | null }) =>
      upload<ImportPreview>(`/api/v1/imports?accountId=${encodeURIComponent(accountId)}`, file),

    // Entregable 10: el mismo archivo, reenviado junto con las columnas que la
    // persona eligió a mano porque ningún parser lo reconoció en el primer intento.
    uploadManual: (
      accountId: string,
      file: { uri: string; name: string; mimeType: string; size?: number | null },
      mapping: ManualColumnMapping,
    ) => {
      const query = toQueryString({
        accountId,
        firstRowIsHeader: String(mapping.firstRowIsHeader),
        dateColumn: mapping.dateColumn,
        descriptionColumn: mapping.descriptionColumn,
        amountColumn: mapping.amountColumn ?? undefined,
        debitColumn: mapping.debitColumn ?? undefined,
        creditColumn: mapping.creditColumn ?? undefined,
        referenceColumn: mapping.referenceColumn ?? undefined,
        saveMapping: String(mapping.saveMapping),
      });
      return upload<ImportPreview>(`/api/v1/imports/manual${query}`, file);
    },

    preview: (importId: string) => request<ImportPreview>(`/api/v1/imports/${importId}`),

    confirm: (importId: string, excludedRowIds: string[] = [], applyDeclaredClosingBalance = false) =>
      request<ImportResult>(`/api/v1/imports/${importId}/confirm`, {
        method: 'POST',
        body: { excludedRowIds, applyDeclaredClosingBalance },
      }),

    cancel: (importId: string) => request<void>(`/api/v1/imports/${importId}`, { method: 'DELETE' }),

    list: (query: { page?: number; pageSize?: number } = {}) =>
      request<Paged<ImportSummary>>(`/api/v1/imports${toQueryString({ ...query })}`),
  },

  emailConnections: {
    list: () => request<EmailConnection[]>('/api/v1/email-connections'),
    start: (providerKind: string, emailAddress: string) =>
      request<EmailConnection>('/api/v1/email-connections', {
        method: 'POST',
        body: { providerKind, emailAddress },
      }),
    revoke: (id: string) => request<void>(`/api/v1/email-connections/${id}`, { method: 'DELETE' }),
  },

  notifications: {
    list: () => request<AppNotification[]>('/api/v1/notifications'),

    markRead: (id: string) => request<void>(`/api/v1/notifications/${id}/read`, { method: 'POST' }),

    registerDevice: (payload: {
      expoPushToken: string;
      platform: string;
      deviceName?: string | null;
      appVersion?: string | null;
    }) =>
      request<NotificationPreferences>('/api/v1/notifications/devices', {
        method: 'POST',
        body: payload,
      }),

    updatePreferences: (token: string, preferences: NotificationPreferences) =>
      request<NotificationPreferences>(
        `/api/v1/notifications/devices/${encodeURIComponent(token)}/preferences`,
        { method: 'PUT', body: preferences },
      ),

    /** Entregable 18: stop pushing to this device -- called best-effort on logout. */
    unregisterDevice: (token: string) =>
      request<void>(`/api/v1/notifications/devices/${encodeURIComponent(token)}`, { method: 'DELETE' }),
  },

  privacy: {
    deleteTransactions: (financialAccountId?: string) =>
      request<{ transactionsDeleted: number }>('/api/v1/privacy/transactions/delete', {
        method: 'POST',
        body: { financialAccountId: financialAccountId ?? null, before: null },
      }),

    deleteAccount: (id: string) =>
      request<{ transactionsDeleted: number }>(`/api/v1/privacy/accounts/${id}`, {
        method: 'DELETE',
      }),

    requestAccountDeletion: () =>
      request<void>('/api/v1/privacy/delete-account', { method: 'POST' }),
  },
};

export { toQueryString };
