import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import {
  useInternalTransferCandidates,
  useMarkNotificationRead,
  useNotifications,
  useSummary,
  useTransactions,
} from '../../hooks/queries';
import { formatCurrency, formatFullDateTime, formatRelativeTime } from '../../utils/format';
import type { AppNotification, HomeSummary, Insight, InternalTransferCandidate, TransactionListItem } from '../../types/api';

type AlertFilter = 'all' | 'priority' | 'insights' | 'review';
type AlertTone = 'neutral' | 'positive' | 'attention' | 'danger' | 'accent';

interface FinancialAlert {
  id: string;
  filter: Exclude<AlertFilter, 'all'>;
  priority: boolean;
  icon: keyof typeof Ionicons.glyphMap;
  tone: AlertTone;
  eyebrow: string;
  title: string;
  subtitle: string;
  detailTitle: string;
  detail: string;
  badge?: string;
  actionLabel: string;
  route?: string;
  notificationId?: string;
}

const FILTERS: { key: AlertFilter; label: string }[] = [
  { key: 'all', label: 'Todas' },
  { key: 'priority', label: 'Prioritarias' },
  { key: 'insights', label: 'Insights' },
  { key: 'review', label: 'Revisadas' },
];

export default function FinancialAlertsScreen() {
  const router = useRouter();
  const summary = useSummary();
  const notifications = useNotifications();
  const transfers = useInternalTransferCandidates();
  const transactions = useTransactions({});
  const markRead = useMarkNotificationRead();

  const [filter, setFilter] = useState<AlertFilter>('all');
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());

  const transactionItems = useMemo(
    () => transactions.data?.pages.flatMap((page) => page.items) ?? [],
    [transactions.data],
  );

  const alerts = useMemo(
    () =>
      buildFinancialAlerts({
        summary: summary.data,
        notifications: notifications.data ?? [],
        transfers: transfers.data ?? [],
        transactions: transactionItems,
      }),
    [notifications.data, summary.data, transactionItems, transfers.data],
  );

  const visibleAlerts = alerts.filter((item) => {
    if (dismissed.has(item.id)) {
      return false;
    }

    return filter === 'all' ? item.filter !== 'review' : item.filter === filter;
  });

  const priorityCount = alerts.filter((item) => item.priority && !dismissed.has(item.id)).length;
  const insightCount = alerts.filter((item) => item.filter === 'insights' && !dismissed.has(item.id)).length;
  const reviewedCount = alerts.filter((item) => item.filter === 'review').length + dismissed.size;
  const isLoading = summary.isLoading || notifications.isLoading || transfers.isLoading || transactions.isLoading;
  const isRefetching = summary.isRefetching || notifications.isRefetching || transfers.isRefetching || transactions.isRefetching;

  const refresh = () => {
    void summary.refetch();
    void notifications.refetch();
    void transfers.refetch();
    void transactions.refetch();
  };

  const markAllReviewed = () => {
    for (const alert of alerts) {
      if (alert.notificationId) {
        markRead.mutate(alert.notificationId);
      }
    }

    setDismissed(new Set(alerts.filter((alert) => !alert.notificationId).map((alert) => alert.id)));
  };

  const handlePrimaryAction = (alert: FinancialAlert) => {
    if (alert.notificationId) {
      markRead.mutate(alert.notificationId);
    }

    if (alert.route) {
      router.push(alert.route);
    }
  };

  return (
    <Screen refreshing={isRefetching} onRefresh={refresh}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.topRow}>
        <Badge label="Radar preventivo Nexo" tone="accent" />
        <Button label="Marcar revisadas" variant="ghost" compact fullWidth={false} onPress={markAllReviewed} />
      </View>

      <View style={styles.header}>
        <Typo variant="title">Alertas financieras</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Situaciones detectadas en tus cuentas para proteger tu dinero con calma y acciones concretas.
        </Typo>
      </View>

      <View style={styles.filters}>
        {FILTERS.map((item) => (
          <FilterPill
            key={item.key}
            label={item.label}
            count={countForFilter(item.key, alerts.length, priorityCount, insightCount, reviewedCount)}
            selected={filter === item.key}
            onPress={() => setFilter(item.key)}
          />
        ))}
      </View>

      {isLoading ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : visibleAlerts.length === 0 ? (
        <EmptyState
          icon="shield-checkmark-outline"
          title="Radar sin pendientes"
          body="No encontramos alertas financieras accionables con la información actual."
          actionLabel="Actualizar"
          onAction={refresh}
        />
      ) : (
        <>
          {visibleAlerts.some((item) => item.priority) ? (
            <View style={styles.section}>
              <SectionHeader title="Requieren tu decisión" />
              <View style={styles.stack}>
                {visibleAlerts.filter((item) => item.priority).map((alert) => (
                  <AlertCard
                    key={alert.id}
                    alert={alert}
                    onPress={() => handlePrimaryAction(alert)}
                    onDismiss={() => setDismissed((current) => new Set(current).add(alert.id))}
                  />
                ))}
              </View>
            </View>
          ) : null}

          {visibleAlerts.some((item) => !item.priority) ? (
            <View style={styles.section}>
              <SectionHeader title={filter === 'review' ? 'Revisadas' : 'Ritmo de gasto y contexto'} />
              <View style={styles.stack}>
                {visibleAlerts.filter((item) => !item.priority).map((alert) => (
                  <AlertCard
                    key={alert.id}
                    alert={alert}
                    onPress={() => handlePrimaryAction(alert)}
                    onDismiss={() => setDismissed((current) => new Set(current).add(alert.id))}
                  />
                ))}
              </View>
            </View>
          ) : null}
        </>
      )}

      <Card tone="secondary" style={styles.footer}>
        <View style={styles.footerIcon}>
          <Ionicons name="leaf-outline" size={20} color={colors.text} />
        </View>
        <Typo variant="bodyStrong">Tranquilidad por diseño</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Esta pantalla usa únicamente datos guardados por Nexo: movimientos, notificaciones, insights, cuentas
          desactualizadas y transferencias sugeridas.
        </Typo>
        <Button
          label="Ajustar notificaciones"
          compact
          variant="secondary"
          fullWidth={false}
          onPress={() => router.push('/notificaciones')}
        />
      </Card>
    </Screen>
  );
}

function buildFinancialAlerts({
  summary,
  notifications,
  transfers,
  transactions,
}: {
  summary?: HomeSummary;
  notifications: AppNotification[];
  transfers: InternalTransferCandidate[];
  transactions: TransactionListItem[];
}): FinancialAlert[] {
  const alerts: FinancialAlert[] = [];

  if (summary) {
    for (const account of summary.staleAccounts.slice(0, 3)) {
      alerts.push({
        id: `stale-account:${account.accountId}`,
        filter: 'priority',
        priority: true,
        icon: 'refresh-outline',
        tone: 'attention',
        eyebrow: 'Cuenta por actualizar',
        title: account.alias,
        subtitle: `Última actualización: ${formatRelativeTime(account.lastSyncedAt)}`,
        detailTitle: 'Saldo con baja confianza',
        detail: 'Importa un extracto reciente o confirma el saldo que ves en el banco para que el cálculo vuelva a ser confiable.',
        badge: 'Acción sugerida',
        actionLabel: 'Actualizar cuenta',
        route: `/cuentas/reconectar?accountId=${account.accountId}`,
      });
    }

    if (summary.monthComparison.expenseChangePercent !== null && summary.monthComparison.expenseChangePercent >= 15) {
      alerts.push({
        id: 'month-expense-pace',
        filter: 'priority',
        priority: true,
        icon: 'trending-up-outline',
        tone: 'attention',
        eyebrow: 'Ritmo de gasto detectado',
        title: `${Math.round(summary.monthComparison.expenseChangePercent)}% más que el mes pasado`,
        subtitle: `Gasto del mes: ${formatCurrency(summary.month.expense)}`,
        detailTitle: 'Revisa el dinero disponible',
        detail: 'El gasto del mes viene por encima del periodo anterior. Nexo puede recalcular cuánto usar sin comprometer el cierre del mes.',
        badge: 'Prioritaria',
        actionLabel: 'Ver dinero disponible',
        route: '/dinero-disponible',
      });
    }

    const topCategory = summary.categoryBreakdown[0];
    if (topCategory) {
      alerts.push({
        id: `top-category:${topCategory.categoryId}`,
        filter: 'insights',
        priority: false,
        icon: 'pie-chart-outline',
        tone: 'neutral',
        eyebrow: 'Concentración de gasto',
        title: topCategory.name,
        subtitle: `${formatCurrency(topCategory.total)} este mes`,
        detailTitle: `${Math.round(topCategory.percentage)}% de tus gastos`,
        detail: `Esta categoría reúne ${topCategory.count} movimiento(s). Revisa el desglose si quieres ajustar tu ritmo.`,
        badge: 'Contexto',
        actionLabel: 'Ver análisis',
        route: '/analisis-gastos',
      });
    }

    for (const insight of summary.insights.slice(0, 4)) {
      alerts.push(fromInsight(insight));
    }
  }

  for (const candidate of transfers.slice(0, 3)) {
    alerts.push({
      id: `transfer:${candidate.outgoingTransactionId}:${candidate.incomingTransactionId}`,
      filter: 'priority',
      priority: true,
      icon: 'swap-horizontal-outline',
      tone: 'accent',
      eyebrow: 'Posible transferencia propia',
      title: `${candidate.outgoingAccountAlias} hacia ${candidate.incomingAccountAlias}`,
      subtitle: `${formatCurrency(candidate.amount)} · ${formatFullDateTime(candidate.outgoingDate)}`,
      detailTitle: 'Evita duplicar ingreso y gasto',
      detail: 'Si confirmas esta pareja, Nexo no la contará como gasto ni como ingreso porque solo moviste dinero entre tus cuentas.',
      badge: 'Confirmar',
      actionLabel: 'Revisar transferencia',
      route: '/transferencias',
    });
  }

  for (const transaction of transactions.filter((item) => item.status === 'NeedsReview').slice(0, 4)) {
    alerts.push({
      id: `needs-review:${transaction.id}`,
      filter: 'priority',
      priority: true,
      icon: 'copy-outline',
      tone: 'attention',
      eyebrow: 'Movimiento por revisar',
      title: transaction.merchant ?? transaction.description,
      subtitle: `${transaction.accountAlias} · ${formatCurrency(transaction.signedAmount, { signed: true })}`,
      detailTitle: 'Podría ser duplicado',
      detail: 'El backend lo conservó separado para que decidas si es un movimiento real o una repetición del banco.',
      badge: 'Decisión',
      actionLabel: 'Abrir movimiento',
      route: `/movimiento/${transaction.id}`,
    });
  }

  for (const notification of notifications.slice(0, 8)) {
    alerts.push({
      id: `notification:${notification.id}`,
      filter: notification.isRead ? 'review' : 'priority',
      priority: !notification.isRead && notification.type === 'SecurityAlert',
      icon: iconForNotification(notification.type),
      tone: notification.type === 'SecurityAlert' ? 'danger' : 'neutral',
      eyebrow: notification.isRead ? 'Notificación revisada' : 'Notificación pendiente',
      title: notification.title,
      subtitle: formatRelativeTime(notification.createdAt),
      detailTitle: notification.type,
      detail: notification.body,
      badge: notification.isRead ? 'Revisada' : 'Nueva',
      actionLabel: notification.isRead ? 'Ver historial' : 'Marcar revisada',
      route: '/notificaciones',
      notificationId: notification.isRead ? undefined : notification.id,
    });
  }

  return alerts;
}

function fromInsight(insight: Insight): FinancialAlert {
  const priority = insight.severity === 'Attention';
  return {
    id: `insight:${insight.code}`,
    filter: 'insights',
    priority,
    icon: iconForInsight(insight.code),
    tone: priority ? 'attention' : insight.severity === 'Positive' ? 'positive' : 'neutral',
    eyebrow: priority ? 'Insight prioritario' : 'Insight financiero',
    title: insight.title,
    subtitle: insight.value === null ? 'Calculado por Nexo' : formatCurrency(insight.value),
    detailTitle: 'Lectura del mes',
    detail: insight.body,
    badge: priority ? 'Atención' : 'Insight',
    actionLabel: routeForInsight(insight.code) === '/dinero-disponible' ? 'Ver disponible' : 'Ver análisis',
    route: routeForInsight(insight.code),
  };
}

function routeForInsight(code: string): string {
  return code === 'INCOME_VS_EXPENSE' || code === 'MONTH_OVER_MONTH' ? '/dinero-disponible' : '/analisis-gastos';
}

function iconForInsight(code: string): keyof typeof Ionicons.glyphMap {
  switch (code) {
    case 'MONTHLY_SPEND':
      return 'wallet-outline';
    case 'MONTH_OVER_MONTH':
      return 'trending-up-outline';
    case 'TOP_CATEGORY':
      return 'pie-chart-outline';
    case 'STALE_ACCOUNT':
      return 'refresh-outline';
    default:
      return 'sparkles-outline';
  }
}

function iconForNotification(type: string): keyof typeof Ionicons.glyphMap {
  switch (type) {
    case 'SecurityAlert':
      return 'shield-checkmark-outline';
    case 'IncomeDetected':
      return 'cash-outline';
    case 'ExpenseDetected':
      return 'card-outline';
    case 'ImportCompleted':
      return 'document-text-outline';
    case 'AccountNeedsUpdate':
      return 'refresh-outline';
    default:
      return 'notifications-outline';
  }
}

function countForFilter(
  filter: AlertFilter,
  allCount: number,
  priorityCount: number,
  insightCount: number,
  reviewedCount: number,
): number {
  switch (filter) {
    case 'all':
      return allCount - reviewedCount;
    case 'priority':
      return priorityCount;
    case 'insights':
      return insightCount;
    case 'review':
      return reviewedCount;
    default:
      return filter satisfies never;
  }
}

function FilterPill({
  label,
  count,
  selected,
  onPress,
}: {
  label: string;
  count: number;
  selected: boolean;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="tab"
      accessibilityState={{ selected }}
      style={[styles.filterPill, selected ? styles.filterPillSelected : null]}
    >
      <Typo variant="caption" color={selected ? colors.onPrimary : colors.textSecondary}>
        {label}
      </Typo>
      <View style={[styles.filterCount, selected ? styles.filterCountSelected : null]}>
        <Typo variant="overline" color={selected ? colors.primary : colors.textSecondary}>
          {count}
        </Typo>
      </View>
    </Pressable>
  );
}

function AlertCard({
  alert,
  onPress,
  onDismiss,
}: {
  alert: FinancialAlert;
  onPress: () => void;
  onDismiss: () => void;
}) {
  const accent = colorForTone(alert.tone);

  return (
    <Card style={styles.alertCard}>
      <View style={styles.alertTop}>
        <View style={[styles.alertIcon, { backgroundColor: `${accent}22` }]}>
          <Ionicons name={alert.icon} size={21} color={accent} />
        </View>
        <View style={styles.flex}>
          <Typo variant="overline" color={accent}>
            {alert.eyebrow}
          </Typo>
          <Typo variant="subheading" numberOfLines={2}>
            {alert.title}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {alert.subtitle}
          </Typo>
        </View>
        {alert.badge ? <Badge label={alert.badge} tone={alert.tone} /> : null}
      </View>

      <View style={styles.detailBox}>
        <View style={styles.inline}>
          <Ionicons name="information-circle-outline" size={17} color={accent} />
          <Typo variant="bodyStrong" style={styles.flex}>
            {alert.detailTitle}
          </Typo>
        </View>
        <Typo variant="caption" color={colors.textSecondary} style={styles.detailText}>
          {alert.detail}
        </Typo>
      </View>

      <View style={styles.cardActions}>
        <Button label={alert.actionLabel} compact fullWidth={false} onPress={onPress} />
        {alert.filter !== 'review' ? (
          <Button label="Descartar" variant="ghost" compact fullWidth={false} onPress={onDismiss} />
        ) : null}
      </View>
    </Card>
  );
}

function colorForTone(tone: AlertTone): string {
  switch (tone) {
    case 'positive':
      return colors.success;
    case 'attention':
      return colors.warning;
    case 'danger':
      return colors.danger;
    case 'accent':
      return colors.accent;
    default:
      return colors.textSecondary;
  }
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  header: {
    gap: spacing.sm,
    marginTop: spacing.md,
    marginBottom: spacing.lg,
  },
  filters: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginBottom: spacing.lg,
  },
  filterPill: {
    minHeight: 38,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
    paddingHorizontal: spacing.md,
  },
  filterPillSelected: {
    backgroundColor: colors.primary,
  },
  filterCount: {
    minWidth: 22,
    height: 22,
    borderRadius: 11,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.surface,
    paddingHorizontal: 5,
  },
  filterCountSelected: {
    backgroundColor: colors.surface,
  },
  section: {
    marginTop: spacing.xl,
  },
  stack: {
    gap: spacing.md,
  },
  alertCard: {
    gap: spacing.md,
  },
  alertTop: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  alertIcon: {
    width: 44,
    height: 44,
    borderRadius: 16,
    alignItems: 'center',
    justifyContent: 'center',
  },
  detailBox: {
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xs,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  detailText: {
    lineHeight: 18,
  },
  cardActions: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  footer: {
    gap: spacing.sm,
    marginTop: spacing.xxl,
  },
  footerIcon: {
    width: 40,
    height: 40,
    borderRadius: 16,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
