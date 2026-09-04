import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type TransactionQuery } from '../services/endpoints';

export const queryKeys = {
  summary: ['summary'] as const,
  accounts: ['accounts'] as const,
  account: (id: string) => ['accounts', id] as const,
  providers: ['providers'] as const,
  categories: ['categories'] as const,
  insights: ['insights'] as const,
  transactions: (query: TransactionQuery) => ['transactions', query] as const,
  transaction: (id: string) => ['transactions', id] as const,
  emailConnections: ['email-connections'] as const,
};

export function useSummary() {
  return useQuery({ queryKey: queryKeys.summary, queryFn: api.summary });
}

export function useAccounts() {
  return useQuery({ queryKey: queryKeys.accounts, queryFn: api.accounts.list });
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

export function useSetCategory(transactionId: string) {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (categoryId: string) => api.transactions.setCategory(transactionId, categoryId, true),
    onSuccess: () => {
      // A recategorisation changes the breakdown and the insights too.
      void client.invalidateQueries({ queryKey: queryKeys.transaction(transactionId) });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.summary });
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

export function useEmailConnections() {
  return useQuery({ queryKey: queryKeys.emailConnections, queryFn: api.emailConnections.list });
}

export function useRefreshAfterImport() {
  const client = useQueryClient();

  return () => {
    void client.invalidateQueries({ queryKey: queryKeys.summary });
    void client.invalidateQueries({ queryKey: queryKeys.accounts });
    void client.invalidateQueries({ queryKey: ['transactions'] });
    void client.invalidateQueries({ queryKey: queryKeys.insights });
  };
}
