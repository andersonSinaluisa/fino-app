import { request, upload } from './apiClient';
import type {
  Account,
  AuthResult,
  Category,
  EmailConnection,
  HomeSummary,
  ImportPreview,
  ImportResult,
  Insight,
  NotificationPreferences,
  Paged,
  Provider,
  TransactionDetail,
  TransactionListItem,
} from '../types/api';

export interface TransactionQuery {
  search?: string;
  accountId?: string;
  categoryId?: string;
  direction?: 'Income' | 'Expense';
  from?: string;
  to?: string;
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

    setVerifiedBalance: (id: string, balance: number) =>
      request<Account>(`/api/v1/accounts/${id}/verified-balance`, {
        method: 'POST',
        body: { balance, asOf: new Date().toISOString() },
      }),

    archive: (id: string) => request<void>(`/api/v1/accounts/${id}`, { method: 'DELETE' }),
  },

  transactions: {
    list: (query: TransactionQuery = {}) =>
      request<Paged<TransactionListItem>>(`/api/v1/transactions${toQueryString({ ...query })}`),

    get: (id: string) => request<TransactionDetail>(`/api/v1/transactions/${id}`),

    setCategory: (id: string, categoryId: string, createRule = true) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/category`, {
        method: 'PUT',
        body: { categoryId, createRule },
      }),

    setNote: (id: string, note: string | null) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/note`, {
        method: 'PUT',
        body: { note },
      }),
  },

  categories: () => request<Category[]>('/api/v1/categories'),

  insights: {
    list: () => request<Insight[]>('/api/v1/insights'),
    refresh: () => request<Insight[]>('/api/v1/insights/refresh', { method: 'POST' }),
  },

  imports: {
    upload: (accountId: string, file: { uri: string; name: string; mimeType: string }) =>
      upload<ImportPreview>(`/api/v1/imports?accountId=${encodeURIComponent(accountId)}`, file),

    preview: (importId: string) => request<ImportPreview>(`/api/v1/imports/${importId}`),

    confirm: (importId: string, excludedRowIds: string[] = [], applyDeclaredClosingBalance = false) =>
      request<ImportResult>(`/api/v1/imports/${importId}/confirm`, {
        method: 'POST',
        body: { excludedRowIds, applyDeclaredClosingBalance },
      }),

    cancel: (importId: string) => request<void>(`/api/v1/imports/${importId}`, { method: 'DELETE' }),
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
