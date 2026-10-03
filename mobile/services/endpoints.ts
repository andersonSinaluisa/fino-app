import { request, upload } from './apiClient';
import type {
  Account,
  ReplaceSplitsRequest,
  BudgetDetail,
  BudgetMovements,
  BudgetOverview,
  BudgetPreview,
  BudgetPreviewRequest,
  CommittedMoney,
  CardMovementType,
  CardPaymentResult,
  CardPaymentSuggestion,
  CreateCreditCardRequest,
  CreateInstallmentPlanRequest,
  CreditCardDetail,
  CreditCardList,
  CreditCardStatement,
  DeclareStatementRequest,
  InstallmentPlan,
  RegisterCardPaymentRequest,
  UpdateCreditCardRequest,
  CreateBudgetRequest,
  UpdateBudgetRequest,
  AnalyticsDashboard,
  AnalyticsPeriodCode,
  AppNotification,
  AuthResult,
  Category,
  CategorizationRule,
  CreateCategorizationRuleRequest,
  CreateCategoryRequest,
  EmailConnection,
  HomeSummary,
  ImportPreview,
  ImportResult,
  ImportSummary,
  Insight,
  ConfirmWithdrawalRequest,
  CreateQuickTransactionRequest,
  QuickEntryBootstrap,
  WithdrawalCandidate,
  WithdrawalConfirmation,
  WithdrawalScan,
  SetCashBalanceRequest,
  UpdateQuickTransactionRequest,
  InternalTransferCandidate,
  ManualColumnMapping,
  NotificationPreferences,
  OnboardingStatus,
  Paged,
  Provider,
  Pulse,
  RulePreview,
  RulePreviewRequest,
  SecurityEvent,
  Session,
  TransactionDetail,
  TransactionListItem,
  UpdateCategorizationRuleRequest,
  UpdateCategoryRequest,
  LegalDocument,
  LegalStatus,
  RegisterConsents,
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
  // Términos, privacidad y consentimientos (LOPDP). Los documentos son
  // públicos: se muestran antes de que exista una cuenta.
  legal: {
    document: (kind: LegalDocument['kind']) =>
      request<LegalDocument>(`/api/v1/legal/${kind}`, { authenticated: false }),
    status: () => request<LegalStatus>('/api/v1/legal/status'),
    accept: (body: { termsVersion: string; privacyVersion: string; confirmedAdult: boolean; analyticsConsent?: boolean | null }) =>
      request<LegalStatus>('/api/v1/legal/accept', { method: 'POST', body }),
    setAnalytics: (granted: boolean) =>
      request<LegalStatus>('/api/v1/legal/analytics', { method: 'PUT', body: { granted } }),
  },

  auth: {
    register: (email: string, password: string, displayName: string, consents: RegisterConsents) =>
      request<AuthResult>('/api/v1/auth/register', {
        method: 'POST',
        authenticated: false,
        body: { email, password, displayName, ...consents },
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
    // rulePattern: la parte de la descripción que la persona eligió para la
    // regla; sin él, el backend usa su sugerencia.
    setCategory: (
      id: string,
      categoryId: string,
      createRule = true,
      applyToExistingMatches = false,
      rulePattern: string | null = null,
    ) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/category`, {
        method: 'PUT',
        body: { categoryId, createRule, applyToExistingMatches, ...(rulePattern ? { rulePattern } : {}) },
      }),

    // Movimientos divididos: siempre la división completa en UNA petición; el
    // backend la valida y la guarda en una sola transacción de base de datos.
    replaceSplits: (id: string, body: ReplaceSplitsRequest) =>
      request<TransactionDetail>(`/api/v1/transactions/${id}/splits`, { method: 'PUT', body }),

    removeSplits: (id: string, categoryId: string | null, expectedVersion?: number) => {
      const params = new URLSearchParams();
      if (categoryId) params.set('categoryId', categoryId);
      if (expectedVersion !== undefined) params.set('expectedVersion', String(expectedVersion));
      const query = params.toString();
      return request<TransactionDetail>(`/api/v1/transactions/${id}/splits${query ? `?${query}` : ''}`, { method: 'DELETE' });
    },

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

  /**
   * Registro rápido de efectivo (§28). Una sola superficie para TODOS los puntos
   * de entrada -- el bottom sheet, el formulario completo, un frecuente de un
   * toque, la voz, el widget y los atajos del sistema operativo. Ninguno de ellos
   * tiene su propia forma de crear un movimiento.
   */
  quickEntry: {
    /** §40: todo lo que el sheet necesita para abrirse, en una sola llamada. */
    bootstrap: () => request<QuickEntryBootstrap>('/api/v1/quick-entry/bootstrap'),

    create: (body: CreateQuickTransactionRequest) =>
      request<TransactionDetail>('/api/v1/quick-entry/transactions', { method: 'POST', body }),

    update: (id: string, body: UpdateQuickTransactionRequest) =>
      request<TransactionDetail>(`/api/v1/quick-entry/transactions/${id}`, { method: 'PUT', body }),

    /**
     * §6: Deshacer. Borra de verdad, y solo funciona sobre movimientos escritos a
     * mano -- los que reporta un banco se ignoran, no se borran.
     */
    remove: (id: string) =>
      request<void>(`/api/v1/quick-entry/transactions/${id}`, { method: 'DELETE' }),

    /** §24-25: declarar cuánto efectivo tienes, o corregirlo dejando rastro. */
    setCashBalance: (body: SetCashBalanceRequest) =>
      request<Account>('/api/v1/quick-entry/cash-balance', { method: 'POST', body }),
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

    /**
     * Retiros de efectivo. Cuelgan de /transfers a propósito: un retiro conciliado
     * ES una transferencia interna, y separarlos sugeriría que hay dos sistemas de
     * conciliación cuando solo hay uno.
     */
    withdrawals: {
      candidates: () =>
        request<WithdrawalCandidate[]>('/api/v1/transfers/withdrawals/candidates'),

      /** Sin cashTransactionId, el servidor crea la entrada de efectivo que falta. */
      confirm: (transactionId: string, body: ConfirmWithdrawalRequest = {}) =>
        request<WithdrawalConfirmation>(`/api/v1/transfers/withdrawals/${transactionId}/confirm`, {
          method: 'POST',
          body,
        }),

      reject: (transactionId: string) =>
        request<void>(`/api/v1/transfers/withdrawals/${transactionId}/reject`, { method: 'POST' }),

      /** Cuántos retiros dejó una importación, para avisar sin interrumpirla. */
      scan: (importId: string) =>
        request<WithdrawalScan>(`/api/v1/transfers/withdrawals/scan/${importId}`),
    },
  },

  categories: {
    list: () => request<Category[]>('/api/v1/categories'),

    // Categorías personalizadas: crear/editar una categoría propia con
    // ícono y color. Nunca para las del sistema (el backend rechaza eso).
    create: (body: CreateCategoryRequest) =>
      request<Category>('/api/v1/categories', { method: 'POST', body }),

    update: (id: string, body: UpdateCategoryRequest) =>
      request<Category>(`/api/v1/categories/${id}`, { method: 'PUT', body }),
  },

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

  /**
   * Presupuestos. `date` is a LOCAL calendar date (yyyy-MM-dd); omitted = today.
   * Every figure (spent, remaining, reserved, level) comes computed from the
   * backend -- the app never re-derives them.
   */
  budgets: {
    list: (date?: string) =>
      request<BudgetOverview>(`/api/v1/budgets${date ? `?date=${encodeURIComponent(date)}` : ''}`),
    get: (id: string, date?: string) =>
      request<BudgetDetail>(`/api/v1/budgets/${id}${date ? `?date=${encodeURIComponent(date)}` : ''}`),
    movements: (id: string, date?: string) =>
      request<BudgetMovements>(`/api/v1/budgets/${id}/movements${date ? `?date=${encodeURIComponent(date)}` : ''}`),
    create: (body: CreateBudgetRequest) => request<BudgetDetail>('/api/v1/budgets', { method: 'POST', body }),
    update: (id: string, body: UpdateBudgetRequest) =>
      request<BudgetDetail>(`/api/v1/budgets/${id}`, { method: 'PUT', body }),
    remove: (id: string) => request<void>(`/api/v1/budgets/${id}`, { method: 'DELETE' }),
    /** Same engine as the real Comprometido: "¿cómo queda mi Disponible si guardo esto?". */
    preview: (body: BudgetPreviewRequest) =>
      request<BudgetPreview>('/api/v1/budgets/preview', { method: 'POST', body }),
  },

  /**
   * Tarjetas de crédito. `id` es el id de la CUENTA de la tarjeta: sus
   * movimientos son `transactions.list({ accountId: id })`, sin endpoint aparte.
   * Deuda, cupo, estados, cuotas y lo que va a Comprometido llegan calculados.
   */
  creditCards: {
    list: (includeArchived = false) =>
      request<CreditCardList>(`/api/v1/credit-cards${includeArchived ? '?includeArchived=true' : ''}`),
    get: (id: string) => request<CreditCardDetail>(`/api/v1/credit-cards/${id}`),
    create: (body: CreateCreditCardRequest) => request<CreditCardDetail>('/api/v1/credit-cards', { method: 'POST', body }),
    update: (id: string, body: UpdateCreditCardRequest) =>
      request<CreditCardDetail>(`/api/v1/credit-cards/${id}`, { method: 'PUT', body }),
    setDebt: (id: string, currentDebt: number) =>
      request<CreditCardDetail>(`/api/v1/credit-cards/${id}/debt`, { method: 'POST', body: { currentDebt } }),
    archive: (id: string) => request<void>(`/api/v1/credit-cards/${id}`, { method: 'DELETE' }),
    restore: (id: string) => request<CreditCardDetail>(`/api/v1/credit-cards/${id}/restore`, { method: 'POST' }),
    statements: (id: string) => request<CreditCardStatement[]>(`/api/v1/credit-cards/${id}/statements`),
    declareStatement: (id: string, body: DeclareStatementRequest) =>
      request<CreditCardStatement[]>(`/api/v1/credit-cards/${id}/statements`, { method: 'PUT', body }),
    removeStatement: (id: string, closingDate: string) =>
      request<CreditCardStatement[]>(`/api/v1/credit-cards/${id}/statements/${encodeURIComponent(closingDate)}`, { method: 'DELETE' }),
    installments: (id: string) => request<InstallmentPlan[]>(`/api/v1/credit-cards/${id}/installments`),
    createInstallmentPlan: (id: string, body: CreateInstallmentPlanRequest) =>
      request<InstallmentPlan>(`/api/v1/credit-cards/${id}/installments`, { method: 'POST', body }),
    cancelInstallmentPlan: (id: string, planId: string) =>
      request<InstallmentPlan>(`/api/v1/credit-cards/${id}/installments/${planId}/cancel`, { method: 'POST' }),
    deleteInstallmentPlan: (id: string, planId: string) =>
      request<void>(`/api/v1/credit-cards/${id}/installments/${planId}`, { method: 'DELETE' }),
    pay: (id: string, body: RegisterCardPaymentRequest) =>
      request<CardPaymentResult>(`/api/v1/credit-cards/${id}/payments`, { method: 'POST', body }),
    paymentSuggestions: (id: string) => request<CardPaymentSuggestion[]>(`/api/v1/credit-cards/${id}/payments/suggestions`),
    linkPayment: (id: string, bankTransactionId: string, cardTransactionId?: string | null) =>
      request<CardPaymentResult>(`/api/v1/credit-cards/${id}/payments/link`, {
        method: 'POST',
        body: { bankTransactionId, cardTransactionId: cardTransactionId ?? null },
      }),
    reclassify: (id: string, transactionId: string, type: CardMovementType) =>
      request<TransactionDetail>(`/api/v1/credit-cards/${id}/movements/${transactionId}/type`, { method: 'POST', body: { type } }),
  },

  finance: {
    /** Tu dinero → Comprometido (con su desglose) → Disponible. Única fuente de verdad. */
    committed: () => request<CommittedMoney>('/api/v1/finance/committed'),
  },

  /** PULSO FASE 1/2: read-only from the app -- PulseEvaluationWorker is what creates pulses server-side. */
  pulses: {
    list: () => request<Pulse[]>('/api/v1/pulses'),
    get: (id: string) => request<Pulse>(`/api/v1/pulses/${id}`),
    /** PULSO FASE 4: 👍/👎 on the detail screen. */
    feedback: (id: string, helpful: boolean) =>
      request<Pulse>(`/api/v1/pulses/${id}/feedback`, { method: 'POST', body: { helpful } }),
  },

  /**
   * Onboarding funcional (rediseño post-login): register() and login()
   * already return the current snapshot inline (AuthResult.user.onboarding),
   * so `get` here is only for refreshing a possibly-stale cached value, not
   * the common path.
   */
  onboarding: {
    get: () => request<OnboardingStatus>('/api/v1/onboarding'),
    start: () => request<OnboardingStatus>('/api/v1/onboarding/start', { method: 'POST' }),
    tutorialCompleted: () =>
      request<OnboardingStatus>('/api/v1/onboarding/tutorial-completed', { method: 'POST' }),
    skip: () => request<OnboardingStatus>('/api/v1/onboarding/skip', { method: 'POST' }),
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

    /** Enlace de 5 minutos para descargar mis datos desde el navegador (sin token de sesión). */
    exportLink: () => request<{ path: string; expiresAt: string }>('/api/v1/privacy/export-link', { method: 'POST' }),
  },
};

export { toQueryString };
