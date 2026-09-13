import { useEffect, useState } from 'react';
import { Modal, Pressable, StyleSheet, View, type LayoutChangeEvent } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, EmptyState, Input, Screen, SelectSheet, Skeleton, SkeletonCard, Typo } from '../../components/ui';
import { IncomeExpenseChart, type IncomeExpenseMode } from '../../components/analytics/IncomeExpenseChart';
import { CategoryTrendList } from '../../components/analytics/CategoryTrendList';
import { useAccounts, useAnalyticsDashboard } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { formatCompactCurrency, formatCurrency, formatPeriodChange, parseDateInput } from '../../utils/format';
import type {
  AnalyticsPeriodCode,
  AnalyticsSeriesPoint,
  BalancePoint,
  CategoryTrend,
  Insight,
  MerchantRanking,
  MonthlyHistoryItem,
  RecurringPayment,
  WeekdayWeekend,
} from '../../types/api';
import { AnalyticsEvent, track } from '../../services/analytics';

const PERIODS: { code: AnalyticsPeriodCode; label: string }[] = [
  { code: 'month', label: 'Este mes' },
  { code: 'last_month', label: 'Mes anterior' },
  { code: 'last_3_months', label: 'Últimos 3 meses' },
  { code: 'last_6_months', label: 'Últimos 6 meses' },
  { code: 'year', label: 'Este año' },
  { code: 'custom', label: 'Personalizado' },
];

interface CustomRange {
  from: string;
  to: string;
}

/**
 * Rediseño de Estadísticas (2026-09): la pantalla sigue siendo la más
 * completa de FINO -- ningún dato se eliminó -- pero ahora explica primero
 * ("¿cómo me fue?") y deja profundizar después, en vez de competir todo al
 * mismo nivel. Reutiliza los mismos hooks y campos reales de
 * AnalyticsDashboard que ya usaba la versión anterior; lo que cambia es la
 * composición visual y qué tan prominente es cada dato.
 */
export default function StatisticsScreen() {
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const [period, setPeriod] = useState<AnalyticsPeriodCode>('month');
  const [accountId, setAccountId] = useState<string | undefined>(undefined);
  const [detailMode, setDetailMode] = useState<IncomeExpenseMode>('weekly');
  const [customRange, setCustomRange] = useState<CustomRange | null>(null);

  const [periodSheetOpen, setPeriodSheetOpen] = useState(false);
  const [accountSheetOpen, setAccountSheetOpen] = useState(false);
  const [customRangeOpen, setCustomRangeOpen] = useState(false);

  const { data: accounts } = useAccounts();
  const { data, isLoading, isError, refetch, isRefetching } = useAnalyticsDashboard({
    period,
    accountId,
    from: period === 'custom' ? customRange?.from : undefined,
    to: period === 'custom' ? customRange?.to : undefined,
  });

  const selectedAccount = accounts?.find((account) => account.id === accountId);
  const accountLabel = selectedAccount?.alias ?? 'Todas las cuentas';

  /**
   * §16: adopción de Estadísticas. `hasData` separa "la abrió y vio algo" de
   * "la abrió y estaba vacía" -- son dos conclusiones muy distintas sobre si
   * la función sirve. Ni el periodo concreto ni la cuenta elegida salen aquí.
   */
  const hasStatistics = Boolean(data && !isError);

  useEffect(() => {
    if (isLoading) {
      return;
    }

    track(AnalyticsEvent.StatisticsViewed, { period, hasData: hasStatistics });
    // Solo al cargar o al cambiar de periodo, no en cada render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLoading, hasStatistics, period]);
  const periodButtonLabel =
    period === 'custom' && customRange
      ? `${shortRangeDate(customRange.from)} - ${shortRangeDate(customRange.to)}`
      : (PERIODS.find((item) => item.code === period)?.label ?? 'Periodo');

  const previousMonthLabel =
    period === 'month' && data && data.monthlyHistory.length >= 2
      ? data.monthlyHistory[data.monthlyHistory.length - 2]!.monthLabel
      : undefined;
  const expenseComparison = data ? formatPeriodChange(data.kpis.expenseChangePercent, previousMonthLabel) : null;

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <View style={styles.header}>
        <View style={styles.headerTitleRow}>
          <View style={styles.headerTitle}>
            <Typo variant="heading">Estadísticas</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              Entiende qué está pasando con tu dinero
            </Typo>
          </View>
          <View style={styles.avatar}>
            <Typo variant="overline">NX</Typo>
          </View>
        </View>

        <View style={styles.filterRow}>
          <FilterButton label={periodButtonLabel} onPress={() => setPeriodSheetOpen(true)} />
          {accounts && accounts.length > 1 ? (
            <FilterButton label={accountLabel} onPress={() => setAccountSheetOpen(true)} />
          ) : null}
        </View>

        {data ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {data.period.label} · {accountLabel}
          </Typo>
        ) : null}
      </View>

      <SelectSheet
        visible={periodSheetOpen}
        title="Periodo"
        options={PERIODS.map((item) => ({ value: item.code as AnalyticsPeriodCode | undefined, label: item.label }))}
        selectedValue={period}
        onSelect={(value) => {
          setPeriodSheetOpen(false);
          if (value === 'custom') {
            setCustomRangeOpen(true);
            return;
          }
          // §16: qué periodos usa de verdad la gente. Solo el código del
          // tramo elegido, nunca las fechas del rango: un rango personalizado
          // puede señalar cuándo cobra o cuándo viajó.
          track(AnalyticsEvent.StatisticsPeriodChanged, { period: value ?? 'month' });
          setPeriod(value ?? 'month');
          setCustomRange(null);
        }}
        onClose={() => setPeriodSheetOpen(false)}
      />

      {accounts && accounts.length > 1 ? (
        <SelectSheet
          visible={accountSheetOpen}
          title="Cuenta"
          options={[
            { value: undefined as string | undefined, label: 'Todas las cuentas' },
            ...accounts.map((account) => ({ value: account.id as string | undefined, label: account.alias })),
          ]}
          selectedValue={accountId}
          onSelect={(value) => {
            // Solo SI filtró por una cuenta o volvió a "todas". Nunca cuál:
            // el id de la cuenta no le dice nada útil a un dashboard y sí es
            // un identificador financiero.
            track(AnalyticsEvent.StatisticsAccountFilterChanged, {
              filterKind: value ? 'single_account' : 'all_accounts',
            });
            setAccountId(value);
            setAccountSheetOpen(false);
          }}
          onClose={() => setAccountSheetOpen(false)}
        />
      ) : null}

      {customRangeOpen ? (
        <CustomRangeModal
          onCancel={() => setCustomRangeOpen(false)}
          onApply={(from, to) => {
            setCustomRange({ from, to });
            setPeriod('custom');
            setCustomRangeOpen(false);
          }}
        />
      ) : null}

      {isError ? (
        <EmptyState
          icon="alert-circle-outline"
          title="No pudimos cargar tus estadísticas"
          body="Revisa tu conexión e inténtalo de nuevo."
          actionLabel="Reintentar"
          onAction={() => void refetch()}
        />
      ) : isLoading || !data ? (
        <View style={styles.loading}>
          <Skeleton height={90} />
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : data.kpis.income === 0 && data.kpis.expense === 0 ? (
        <EmptyState
          icon="stats-chart-outline"
          title="Todavía no hay movimientos"
          body="Importa un estado de cuenta o elige otro periodo para ver tus estadísticas."
        />
      ) : (
        <>
          <SummaryCard
            income={data.kpis.income}
            expense={data.kpis.expense}
            net={data.kpis.net}
            savingsRatePercent={data.kpis.savingsRatePercent}
            comparison={expenseComparison}
            hidden={hidden}
          />

          <View style={styles.section}>
            <Card>
              <Typo variant="bodyStrong">Tu mes</Typo>
              <Typo variant="caption" color={colors.textSecondary} style={styles.cardSubtitle}>
                Ingresos y gastos a lo largo del tiempo
              </Typo>
              <IncomeExpenseChart
                key={`${detailMode}-${period}-${accountId ?? 'all'}`}
                points={detailMode === 'weekly' ? data.series : data.monthlyHistory.map(monthToSeriesPoint)}
                mode={detailMode}
                onModeChange={setDetailMode}
                hidden={hidden}
              />
            </Card>
          </View>

          {data.categoryBreakdown.length > 0 ? (
            <SpendBreakdownCard
              items={data.categoryBreakdown}
              spotlightCategoryId={data.spotlightCategoryId}
              totalExpense={data.kpis.expense}
              periodLabel={data.period.label}
              hidden={hidden}
            />
          ) : null}

          {data.balanceEvolution.length > 0 ? (
            <BalanceEvolutionCard points={data.balanceEvolution} periodLabel={data.period.label} hidden={hidden} />
          ) : null}

          <SpendTypeCard
            fixedExpense={data.moneyFlow.fixedExpense}
            variableExpense={data.moneyFlow.variableExpense}
            recurring={data.recurringPayments}
            hidden={hidden}
          />

          {data.topMerchants.length > 0 ? <TopMerchantsCard merchants={data.topMerchants} hidden={hidden} /> : null}

          <PatternInsightCard data={data.weekdayWeekend} hidden={hidden} />

          {data.monthlyHistory.length > 1 ? <MonthComparisonTable months={data.monthlyHistory} hidden={hidden} /> : null}

          {data.insights.length > 0 ? <DetectedInsightsCard insights={data.insights} /> : null}
        </>
      )}
    </Screen>
  );
}

function monthToSeriesPoint(month: MonthlyHistoryItem): AnalyticsSeriesPoint {
  return {
    from: month.monthLabel,
    to: month.monthLabel,
    label: month.monthLabel,
    income: month.income,
    expense: month.expense,
  };
}

function shortRangeDate(iso: string): string {
  const date = new Date(`${iso}T00:00:00`);
  return `${date.getDate()}/${date.getMonth() + 1}`;
}

function toISODate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function FilterButton({ label, onPress }: { label: string; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.filterButton, pressed ? styles.filterButtonPressed : null]}
    >
      <Typo variant="caption" color={colors.text} numberOfLines={1} style={styles.filterButtonLabel}>
        {label}
      </Typo>
      <Ionicons name="chevron-down" size={14} color={colors.textSecondary} />
    </Pressable>
  );
}

function CustomRangeModal({ onCancel, onApply }: { onCancel: () => void; onApply: (from: string, to: string) => void }) {
  const [fromText, setFromText] = useState('');
  const [toText, setToText] = useState('');
  const [error, setError] = useState<string | null>(null);

  const handleApply = () => {
    const from = parseDateInput(fromText);
    const to = parseDateInput(toText);

    if (!from || !to) {
      setError('Ingresa fechas válidas (DD/MM/AAAA).');
      return;
    }

    if (from.getTime() > to.getTime()) {
      setError('"Desde" debe ser anterior a "Hasta".');
      return;
    }

    setError(null);
    onApply(toISODate(from), toISODate(to));
  };

  return (
    <Modal visible transparent animationType="slide" onRequestClose={onCancel}>
      <Pressable style={styles.backdrop} onPress={onCancel} accessibilityRole="button" accessibilityLabel="Cerrar" />
      <View style={styles.sheet}>
        <View style={styles.handle} />
        <Typo variant="bodyStrong" style={styles.sheetTitle}>
          Periodo personalizado
        </Typo>

        <View style={styles.customField}>
          <Input label="Desde" placeholder="DD/MM/AAAA" value={fromText} onChangeText={setFromText} keyboardType="numeric" />
        </View>
        <View style={styles.customField}>
          <Input
            label="Hasta"
            placeholder="DD/MM/AAAA"
            value={toText}
            onChangeText={setToText}
            keyboardType="numeric"
            error={error}
          />
        </View>

        <View style={styles.customActions}>
          <Pressable onPress={onCancel} hitSlop={8}>
            <Typo variant="body" color={colors.textSecondary}>
              Cancelar
            </Typo>
          </Pressable>
          <Button label="Aplicar" onPress={handleApply} compact fullWidth={false} />
        </View>
      </View>
    </Modal>
  );
}

function SummaryCard({
  income,
  expense,
  net,
  savingsRatePercent,
  comparison,
  hidden,
}: {
  income: number;
  expense: number;
  net: number;
  savingsRatePercent: number | null;
  comparison: { label: string; up: boolean } | null;
  hidden: boolean;
}) {
  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="overline" color={colors.textSecondary} style={styles.cardHeading}>
          RESUMEN DEL MES
        </Typo>

        <View style={styles.summaryRows}>
          <SummaryRow label="Ingresos" value={income} tone={colors.success} hidden={hidden} />
          <View style={styles.divider} />
          <SummaryRow label="Gastos" value={expense} tone={colors.warning} hidden={hidden} />
          <View style={styles.divider} />
          <SummaryRow label="Balance" value={net} tone={net >= 0 ? colors.success : colors.danger} hidden={hidden} strong />
          <View style={styles.divider} />
          <SummaryRow
            label="Ahorro"
            display={savingsRatePercent === null ? '—' : `${Math.round(savingsRatePercent)}%`}
            tone={colors.text}
            hidden={hidden}
          />
        </View>

        {comparison ? (
          <View style={styles.comparisonRow}>
            <Ionicons
              name={comparison.up ? 'trending-up' : 'trending-down'}
              size={13}
              color={comparison.up ? colors.warning : colors.success}
            />
            <Typo variant="caption" color={comparison.up ? colors.warning : colors.success}>
              Gastaste {comparison.label}
            </Typo>
          </View>
        ) : null}
      </Card>
    </View>
  );
}

function SummaryRow({
  label,
  value,
  display,
  tone,
  strong,
  hidden,
}: {
  label: string;
  value?: number;
  display?: string;
  tone: string;
  strong?: boolean;
  hidden: boolean;
}) {
  return (
    <View style={styles.summaryRow}>
      <Typo variant="body" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular color={tone}>
        {hidden ? '••••' : (display ?? formatCurrency(value ?? 0))}
      </Typo>
    </View>
  );
}

function SpendBreakdownCard({
  items,
  spotlightCategoryId,
  totalExpense,
  periodLabel,
  hidden,
}: {
  items: CategoryTrend[];
  spotlightCategoryId: string | null;
  totalExpense: number;
  periodLabel: string;
  hidden: boolean;
}) {
  const [expanded, setExpanded] = useState(false);
  const visible = expanded ? items : items.slice(0, 5);

  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="bodyStrong">¿En qué se fue tu dinero?</Typo>
        <Typo variant="caption" color={colors.textSecondary} style={styles.cardSubtitle}>
          {hidden ? '••••' : formatCurrency(totalExpense)} gastados en {periodLabel}
        </Typo>

        <CategoryTrendList items={visible} spotlightCategoryId={spotlightCategoryId} hidden={hidden} />

        {items.length > 5 ? (
          <Pressable onPress={() => setExpanded((prev) => !prev)} hitSlop={8} style={styles.linkRow}>
            <Typo variant="caption" color={colors.text}>
              {expanded ? 'Ver menos' : 'Ver todas las categorías'}
            </Typo>
          </Pressable>
        ) : null}
      </Card>
    </View>
  );
}

function BalanceEvolutionCard({ points, periodLabel, hidden }: { points: BalancePoint[]; periodLabel: string; hidden: boolean }) {
  const [width, setWidth] = useState(0);
  const values = points.map((point) => point.balance);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min || 1;
  const last = points[points.length - 1]!;
  const chartWidth = Math.max(width - spacing.lg * 2, 1);
  const chartHeight = 116;
  const plotted = points.slice(-6).map((point, index, source) => ({
    point,
    x: source.length === 1 ? chartWidth / 2 : (index / (source.length - 1)) * chartWidth,
    y: chartHeight - ((point.balance - min) / range) * chartHeight,
  }));

  return (
    <View style={styles.section}>
      <Card>
        <View style={styles.cardTitleRow}>
          <View style={styles.flex}>
            <Typo variant="bodyStrong">Tu dinero disponible</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              Cómo cambió tu saldo durante {periodLabel}
            </Typo>
          </View>
          <Typo variant="bodyStrong" tabular>
            {hidden ? '••••' : formatCurrency(last.balance)}
          </Typo>
        </View>

        <View style={styles.lineChart} onLayout={(event: LayoutChangeEvent) => setWidth(event.nativeEvent.layout.width)}>
          {width > 0
            ? plotted.slice(0, -1).map((item, index) => {
                const next = plotted[index + 1]!;
                const dx = next.x - item.x;
                const dy = next.y - item.y;
                const length = Math.sqrt(dx * dx + dy * dy);
                const angle = Math.atan2(dy, dx);
                return (
                  <View
                    key={`${item.point.asOf}-${next.point.asOf}`}
                    style={[
                      styles.lineSegment,
                      {
                        width: length,
                        left: item.x + spacing.lg,
                        top: item.y + spacing.lg,
                        transform: [{ rotateZ: `${angle}rad` }],
                      },
                    ]}
                  />
                );
              })
            : null}
          {width > 0
            ? plotted.map((item) => (
                <View
                  key={item.point.asOf}
                  style={[
                    styles.lineDot,
                    {
                      left: item.x + spacing.lg - 4,
                      top: item.y + spacing.lg - 4,
                    },
                  ]}
                />
              ))
            : null}
        </View>
      </Card>
    </View>
  );
}

function SpendTypeCard({
  fixedExpense,
  variableExpense,
  recurring,
  hidden,
}: {
  fixedExpense: number;
  variableExpense: number;
  recurring: RecurringPayment[];
  hidden: boolean;
}) {
  const hasRecurring = recurring.length > 0;

  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="bodyStrong" style={styles.cardHeadingSpaced}>
          Tipo de gasto
        </Typo>

        <View style={styles.summaryRow}>
          <Typo variant="body" color={colors.textSecondary}>
            Fijos / recurrentes
          </Typo>
          <Typo variant="body" tabular>
            {hidden ? '••••' : formatCurrency(fixedExpense)}
          </Typo>
        </View>
        <View style={styles.summaryRow}>
          <Typo variant="body" color={colors.textSecondary}>
            Variables
          </Typo>
          <Typo variant="body" tabular>
            {hidden ? '••••' : formatCurrency(variableExpense)}
          </Typo>
        </View>

        <View style={styles.spendTypeTrack}>
          <View style={[styles.spendTypeFill, { flex: fixedExpense || 0.0001, backgroundColor: colors.primary }]} />
          <View
            style={[styles.spendTypeFill, { flex: variableExpense || 0.0001, backgroundColor: colors.accentSecondary }]}
          />
        </View>

        {hasRecurring ? (
          <View style={styles.recurringList}>
            {recurring.slice(0, 3).map((item, index) => (
              <View key={item.merchant} style={[styles.listRow, index > 0 ? styles.rowDivider : null]}>
                <Typo variant="caption" numberOfLines={1} style={styles.flex}>
                  {item.merchant}
                </Typo>
                <Typo variant="caption" tabular>
                  {hidden ? '••••' : formatCurrency(item.averageAmount)}
                </Typo>
              </View>
            ))}
          </View>
        ) : (
          <Typo variant="caption" color={colors.textSecondary} style={styles.cardSubtitle}>
            No hemos identificado gastos recurrentes todavía.
          </Typo>
        )}
      </Card>
    </View>
  );
}

function TopMerchantsCard({ merchants, hidden }: { merchants: MerchantRanking[]; hidden: boolean }) {
  const [expanded, setExpanded] = useState(false);
  const visible = expanded ? merchants.slice(0, 8) : merchants.slice(0, 3);
  const max = Math.max(1, ...merchants.map((item) => item.total));

  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="bodyStrong" style={styles.cardHeadingSpaced}>
          Donde más gastaste
        </Typo>

        <View style={styles.merchantList}>
          {visible.map((merchant, index) => (
            <View key={merchant.merchant} style={styles.merchantRow}>
              <Typo variant="caption" color={colors.textSecondary} style={styles.rank}>
                {index + 1}
              </Typo>
              <View style={styles.flex}>
                <Typo variant="body" numberOfLines={1}>
                  {merchant.merchant}
                </Typo>
                <View style={styles.merchantTrack}>
                  <View style={[styles.merchantFill, { width: `${Math.max(5, (merchant.total / max) * 100)}%` }]} />
                </View>
              </View>
              <Typo variant="bodyStrong" tabular>
                {hidden ? '••••' : formatCurrency(merchant.total)}
              </Typo>
            </View>
          ))}
        </View>

        {merchants.length > 3 ? (
          <Pressable onPress={() => setExpanded((prev) => !prev)} hitSlop={8} style={styles.linkRow}>
            <Typo variant="caption" color={colors.text}>
              {expanded ? 'Ver menos' : 'Ver ranking completo'}
            </Typo>
          </Pressable>
        ) : null}
      </Card>
    </View>
  );
}

function PatternInsightCard({ data, hidden }: { data: WeekdayWeekend; hidden: boolean }) {
  const totalSpend = data.weekdayTotal + data.weekendTotal;

  if (totalSpend <= 0) {
    return null;
  }

  const weekendPercent = Math.round((data.weekendTotal / totalSpend) * 100);

  return (
    <View style={styles.section}>
      <Card>
        <View style={styles.insightHeader}>
          <Typo variant="body">✦</Typo>
          <Typo variant="bodyStrong">Patrón detectado</Typo>
        </View>
        <Typo variant="body" color={colors.textSecondary} style={styles.cardSubtitle}>
          Los fines de semana representan {weekendPercent}% de tus gastos variables.
        </Typo>
        <View style={styles.twoColumns}>
          <PatternMetric label="Entre semana" value={data.weekdayAveragePerDay} hidden={hidden} />
          <PatternMetric label="Fin de semana" value={data.weekendAveragePerDay} hidden={hidden} />
        </View>
      </Card>
    </View>
  );
}

function PatternMetric({ label, value, hidden }: { label: string; value: number; hidden: boolean }) {
  return (
    <View style={styles.flex}>
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="body" tabular>
        {hidden ? '••••' : `${formatCurrency(value)} promedio`}
      </Typo>
    </View>
  );
}

function MonthComparisonTable({ months, hidden }: { months: MonthlyHistoryItem[]; hidden: boolean }) {
  const current = months[months.length - 1]!;
  const previous = months[months.length - 2]!;
  const rows: { label: string; current: number; previous: number; goodWhenUp: boolean }[] = [
    { label: 'Ingresos', current: current.income, previous: previous.income, goodWhenUp: true },
    { label: 'Gastos', current: current.expense, previous: previous.expense, goodWhenUp: false },
    { label: 'Balance', current: current.net, previous: previous.net, goodWhenUp: true },
  ];

  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="bodyStrong" style={styles.cardHeadingSpaced}>
          {current.monthLabel} vs {previous.monthLabel}
        </Typo>

        <View style={styles.compareHeaderRow}>
          <Typo variant="overline" color={colors.textSecondary} style={styles.compareLabelCol} />
          <Typo variant="overline" color={colors.textSecondary} style={styles.compareValueCol}>
            {previous.monthLabel.toUpperCase()}
          </Typo>
          <Typo variant="overline" color={colors.textSecondary} style={styles.compareValueCol}>
            {current.monthLabel.toUpperCase()}
          </Typo>
          <Typo variant="overline" color={colors.textSecondary} style={styles.compareChangeCol}>
            CAMBIO
          </Typo>
        </View>

        {rows.map((row) => {
          const change = row.previous === 0 ? null : ((row.current - row.previous) / Math.abs(row.previous)) * 100;
          const favorable = change === null ? null : row.goodWhenUp ? change >= 0 : change <= 0;
          return (
            <View key={row.label} style={styles.compareDataRow}>
              <Typo variant="body" style={styles.compareLabelCol} numberOfLines={1}>
                {row.label}
              </Typo>
              <Typo variant="caption" tabular style={styles.compareValueCol}>
                {hidden ? '••••' : formatCompactCurrency(row.previous)}
              </Typo>
              <Typo variant="caption" tabular style={styles.compareValueCol}>
                {hidden ? '••••' : formatCompactCurrency(row.current)}
              </Typo>
              <Typo
                variant="caption"
                tabular
                color={change === null ? colors.textSecondary : favorable ? colors.success : colors.warning}
                style={styles.compareChangeCol}
              >
                {change === null ? '—' : `${change >= 0 ? '+' : ''}${Math.round(change)}%`}
              </Typo>
            </View>
          );
        })}
      </Card>
    </View>
  );
}

function DetectedInsightsCard({ insights }: { insights: Insight[] }) {
  const [expanded, setExpanded] = useState(false);
  const visible = expanded ? insights : insights.slice(0, 3);

  return (
    <View style={styles.section}>
      <Card>
        <Typo variant="bodyStrong" style={styles.cardHeadingSpaced}>
          Lo que FINO detectó
        </Typo>

        <View style={styles.insightList}>
          {visible.map((insight) => (
            <View key={insight.code} style={styles.insightRow}>
              <Typo variant="body">✦</Typo>
              <View style={styles.flex}>
                <Typo variant="body" numberOfLines={1}>
                  {insight.title}
                </Typo>
                <Typo variant="caption" color={colors.textSecondary}>
                  {insight.body}
                </Typo>
              </View>
            </View>
          ))}
        </View>

        {insights.length > 3 ? (
          <Pressable onPress={() => setExpanded((prev) => !prev)} hitSlop={8} style={styles.linkRow}>
            <Typo variant="caption" color={colors.text}>
              {expanded ? 'Ver menos' : `Ver los ${insights.length} insights`}
            </Typo>
          </Pressable>
        ) : null}
      </Card>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  header: {
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  headerTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  headerTitle: {
    flex: 1,
    gap: 2,
  },
  avatar: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  filterRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  filterButton: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.xs,
    backgroundColor: colors.surface,
    borderRadius: radius.pill,
    borderWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.md,
  },
  filterButtonPressed: {
    opacity: 0.7,
  },
  filterButtonLabel: {
    flexShrink: 1,
  },
  loading: {
    gap: spacing.lg,
    marginTop: spacing.lg,
  },
  section: {
    marginTop: spacing.xl,
  },
  cardHeading: {
    marginBottom: spacing.md,
  },
  cardHeadingSpaced: {
    marginBottom: spacing.md,
  },
  cardSubtitle: {
    marginTop: 2,
    marginBottom: spacing.md,
  },
  cardTitleRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  summaryRows: {
    gap: spacing.sm,
  },
  summaryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: spacing.xs,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  comparisonRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginTop: spacing.md,
  },
  linkRow: {
    marginTop: spacing.md,
    alignItems: 'center',
  },
  lineChart: {
    height: 148,
    position: 'relative',
  },
  lineSegment: {
    position: 'absolute',
    height: 2,
    borderRadius: 1,
    backgroundColor: colors.primary,
    transformOrigin: 'left center',
  },
  lineDot: {
    position: 'absolute',
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.accent,
    borderWidth: 2,
    borderColor: colors.primary,
  },
  spendTypeTrack: {
    height: 10,
    borderRadius: 5,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
    flexDirection: 'row',
    marginTop: spacing.sm,
    marginBottom: spacing.md,
  },
  spendTypeFill: {
    height: '100%',
  },
  recurringList: {
    gap: 0,
  },
  listRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.sm,
  },
  rowDivider: {
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  merchantList: {
    gap: spacing.md,
  },
  merchantRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  rank: {
    width: 18,
  },
  merchantTrack: {
    height: 5,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: 3,
    overflow: 'hidden',
    marginTop: spacing.xs,
  },
  merchantFill: {
    height: '100%',
    borderRadius: 3,
    backgroundColor: colors.accent,
  },
  twoColumns: {
    flexDirection: 'row',
    gap: spacing.lg,
    marginTop: spacing.md,
  },
  insightHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: spacing.sm,
  },
  insightList: {
    gap: spacing.md,
  },
  insightRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  compareHeaderRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginBottom: spacing.xs,
  },
  compareDataRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: spacing.sm,
  },
  compareLabelCol: {
    flex: 1.2,
  },
  compareValueCol: {
    flex: 1,
    textAlign: 'right',
  },
  compareChangeCol: {
    flex: 0.8,
    textAlign: 'right',
  },
  backdrop: {
    flex: 1,
    backgroundColor: colors.overlay,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.xxl,
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.md,
  },
  sheetTitle: {
    marginBottom: spacing.md,
  },
  customField: {
    marginBottom: spacing.md,
  },
  customActions: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: spacing.sm,
  },
});
