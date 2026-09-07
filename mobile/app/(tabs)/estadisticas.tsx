import { useMemo, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View, type LayoutChangeEvent } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Card, EmptyState, Screen, Skeleton, SkeletonCard, Typo } from '../../components/ui';
import { useAccounts, useAnalyticsDashboard } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { formatCompactCurrency, formatCurrency, formatPeriodChange } from '../../utils/format';
import { iconForCategory } from '../../utils/categoryIcons';
import type {
  AnalyticsDashboard,
  AnalyticsPeriodCode,
  AnalyticsSeriesPoint,
  BalancePoint,
  CategoryTrend,
  Insight,
  MerchantRanking,
  MonthlyHistoryItem,
  RecurringPayment,
} from '../../types/api';

const PERIODS: { code: AnalyticsPeriodCode; label: string }[] = [
  { code: 'month', label: 'Este mes' },
  { code: 'last_month', label: 'Mes anterior' },
  { code: 'last_3_months', label: 'Últimos 3 meses' },
  { code: 'last_6_months', label: '6 meses' },
  { code: 'year', label: 'Año' },
];

type DetailMode = 'weekly' | 'monthly';

export default function StatisticsScreen() {
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const [period, setPeriod] = useState<AnalyticsPeriodCode>('month');
  const [accountId, setAccountId] = useState<string | undefined>(undefined);
  const [detailMode, setDetailMode] = useState<DetailMode>('weekly');

  const { data: accounts } = useAccounts();
  const { data, isLoading, isError, refetch, isRefetching } = useAnalyticsDashboard({ period, accountId });

  const selectedAccount = accounts?.find((account) => account.id === accountId);
  const score = useMemo(() => financialScore(data), [data]);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <View style={styles.header}>
        <View style={styles.headerTitleRow}>
          <View style={styles.headerTitle}>
            <View style={styles.inline}>
              <Typo variant="heading">Dashboard financiero</Typo>
              <View style={styles.statusDot}>
                <Ionicons name="checkmark" size={10} color={colors.onAccent} />
              </View>
            </View>
            <Typo variant="caption" color={colors.textSecondary}>
              Analítica transparente de tu dinero
            </Typo>
          </View>
          <View style={styles.avatar}>
            <Typo variant="overline">NX</Typo>
          </View>
        </View>

        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
          {PERIODS.map((item) => (
            <SegmentChip
              key={item.code}
              label={item.label}
              selected={period === item.code}
              onPress={() => setPeriod(item.code)}
            />
          ))}
        </ScrollView>

        {accounts && accounts.length > 1 ? (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
            <SegmentChip label="Todas las cuentas" selected={!accountId} onPress={() => setAccountId(undefined)} />
            {accounts.map((account) => (
              <SegmentChip
                key={account.id}
                label={account.alias}
                selected={accountId === account.id}
                onPress={() => setAccountId(account.id)}
              />
            ))}
          </ScrollView>
        ) : null}
      </View>

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
          <Skeleton height={120} />
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
          <HealthCard data={data} score={score} hidden={hidden} accountLabel={selectedAccount?.alias ?? 'Todas las cuentas'} />

          <View style={styles.kpiGrid}>
            <KpiTile
              label="Ingresos"
              icon="arrow-down"
              value={data.kpis.income}
              tone={colors.success}
              change={data.kpis.incomeChangePercent}
              hidden={hidden}
              signed
            />
            <KpiTile
              label="Gastos totales"
              icon="arrow-up"
              value={data.kpis.expense}
              tone={colors.warning}
              change={data.kpis.expenseChangePercent}
              hidden={hidden}
              invertChangeTone
              signed
            />
            <KpiTile
              label="Balance neto"
              badge={data.kpis.net >= 0 ? 'superávit' : 'déficit'}
              value={data.kpis.net}
              tone={data.kpis.net >= 0 ? colors.success : colors.danger}
              change={data.kpis.netChangePercent}
              hidden={hidden}
              signed
            />
            <KpiTile
              label="Tasa de ahorro"
              icon="leaf"
              value={data.kpis.savingsRatePercent}
              tone={colors.text}
              hidden={hidden}
              percent
            />
          </View>

          <MoneyQuestionCard data={data} hidden={hidden} />
          <IncomeExpenseCard data={data} mode={detailMode} onModeChange={setDetailMode} hidden={hidden} />

          {data.balanceEvolution.length > 0 ? (
            <BalanceEvolutionCard points={data.balanceEvolution} hidden={hidden} />
          ) : null}

          {data.categoryBreakdown.length > 0 ? (
            <CategoryCard items={data.categoryBreakdown} spotlightCategoryId={data.spotlightCategoryId} hidden={hidden} />
          ) : null}

          <SpendStructureCard recurring={data.recurringPayments} hidden={hidden} />

          {data.topMerchants.length > 0 || data.peakSpendingDays.length > 0 ? (
            <MerchantsAndPeaksCard merchants={data.topMerchants} months={data.monthlyHistory} hidden={hidden} />
          ) : null}

          <HabitPatternCard data={data} hidden={hidden} />

          {data.monthlyHistory.length > 1 ? <MonthComparisonCard months={data.monthlyHistory} hidden={hidden} /> : null}

          {data.insights.length > 0 ? <ConclusionsCard insights={data.insights} /> : null}
        </>
      )}
    </Screen>
  );
}

function financialScore(data: AnalyticsDashboard | undefined): number {
  if (!data) {
    return 0;
  }

  const savingsScore = Math.max(0, Math.min(45, (data.kpis.savingsRatePercent ?? 0) * 1.5));
  const balanceScore = data.kpis.net >= 0 ? 30 : Math.max(0, 30 + (data.kpis.net / Math.max(data.kpis.income, 1)) * 30);
  const diversityScore = Math.min(15, data.categoryBreakdown.length * 2);
  const recurrentScore = data.recurringPayments.length > 0 ? 8 : 4;
  return Math.round(Math.max(0, Math.min(100, savingsScore + balanceScore + diversityScore + recurrentScore)));
}

function SegmentChip({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={[styles.segmentChip, selected ? styles.segmentChipSelected : null]}
    >
      <Typo variant="caption" color={selected ? colors.onPrimary : colors.textSecondary} numberOfLines={1}>
        {label}
      </Typo>
    </Pressable>
  );
}

function HealthCard({
  data,
  score,
  hidden,
  accountLabel,
}: {
  data: AnalyticsDashboard;
  score: number;
  hidden: boolean;
  accountLabel: string;
}) {
  return (
    <Card style={styles.healthCard}>
      <View style={styles.healthTop}>
        <View>
          <Typo variant="caption" color={colors.textSecondary}>
            Todo al día - {accountLabel}
          </Typo>
          <Typo variant="bodyStrong">Tu radiografía financiera</Typo>
        </View>
        <Badge label={data.period.label} tone="neutral" />
      </View>
      <View style={styles.scoreRow}>
        <Typo variant="display" tabular>
          {hidden ? '••••' : score}
        </Typo>
        <Typo variant="caption" color={colors.textSecondary} style={styles.scoreText}>
          Estado financiero {score >= 75 ? 'saludable' : score >= 55 ? 'estable' : 'por ajustar'}
        </Typo>
      </View>
      <View style={styles.scoreTrack}>
        <View style={[styles.scoreFill, { width: `${Math.max(5, score)}%` }]} />
      </View>
    </Card>
  );
}

function KpiTile({
  label,
  icon,
  badge,
  value,
  tone,
  change,
  hidden,
  signed = false,
  percent = false,
  invertChangeTone = false,
}: {
  label: string;
  icon?: keyof typeof Ionicons.glyphMap;
  badge?: string;
  value: number | null;
  tone: string;
  change?: number | null;
  hidden: boolean;
  signed?: boolean;
  percent?: boolean;
  invertChangeTone?: boolean;
}) {
  const comparison = change === undefined ? null : formatPeriodChange(change);
  const changeIsGood = comparison ? (invertChangeTone ? !comparison.up : comparison.up) : null;

  return (
    <Card style={styles.kpiTile}>
      <View style={styles.kpiTop}>
        <Typo variant="caption" color={colors.textSecondary}>
          {label}
        </Typo>
        {icon ? <Ionicons name={icon} size={15} color={tone} /> : null}
        {badge ? <Badge label={badge} tone={value !== null && value >= 0 ? 'positive' : 'danger'} /> : null}
      </View>
      <Typo variant="heading" color={tone} tabular numberOfLines={1}>
        {hidden ? '••••' : formatKpiValue(value, { signed, percent })}
      </Typo>
      {comparison ? (
        <Typo variant="caption" color={changeIsGood ? colors.success : colors.warning} numberOfLines={1}>
          {comparison.label}
        </Typo>
      ) : (
        <Typo variant="caption" color={colors.textSecondary}>
          Sin comparativo
        </Typo>
      )}
    </Card>
  );
}

function formatKpiValue(value: number | null, options: { signed?: boolean; percent?: boolean } = {}): string {
  if (value === null) {
    return 'Sin dato';
  }

  if (options.percent) {
    return `${Math.round(value)}%`;
  }

  if (!options.signed) {
    return formatCompactCurrency(value);
  }

  return `${value >= 0 ? '+' : '-'}${formatCompactCurrency(Math.abs(value))}`;
}

function MoneyQuestionCard({ data, hidden }: { data: AnalyticsDashboard; hidden: boolean }) {
  const income = data.moneyFlow.income;
  const fixed = data.moneyFlow.fixedExpense;
  const variable = data.moneyFlow.variableExpense;
  const savings = data.moneyFlow.netSavings;
  const totalExpense = Math.max(fixed + variable, 1);

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">¿Qué pasó con lo que recibí?</Typo>
        <Ionicons name="eye-outline" size={17} color={colors.textSecondary} />
      </View>
      <Typo variant="caption" color={colors.textSecondary}>
        Detalle de cada dólar que ingresó al periodo
      </Typo>

      <View style={styles.flowRibbon}>
        <FlowMetric label="Ingresaste" value={income} hidden={hidden} />
        <FlowMetric label="Gastos fijos" value={fixed} hidden={hidden} />
        <FlowMetric label="Consumo" value={variable} hidden={hidden} />
        <FlowMetric label="Ahorro" value={savings} hidden={hidden} positive={savings >= 0} />
      </View>

      <View style={styles.multiTrack}>
        <View style={[styles.trackPart, { flex: fixed || 0.001, backgroundColor: colors.primary }]} />
        <View style={[styles.trackPart, { flex: variable || 0.001, backgroundColor: colors.accentSecondary }]} />
        <View style={[styles.trackPart, { flex: Math.max(savings, 0) || 0.001, backgroundColor: colors.accent }]} />
      </View>

      <View style={styles.tipBox}>
        <Ionicons name="sparkles-outline" size={17} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
          Transparencia: {Math.round((fixed / totalExpense) * 100)}% de tus gastos fueron fijos y{' '}
          {Math.round((variable / totalExpense) * 100)}% variables. Esta vista usa tus movimientos confirmados.
        </Typo>
      </View>
    </Card>
  );
}

function FlowMetric({ label, value, hidden, positive = false }: { label: string; value: number; hidden: boolean; positive?: boolean }) {
  return (
    <View style={styles.flowMetric}>
      <Typo variant="overline" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="caption" color={positive ? colors.success : colors.text} tabular numberOfLines={1}>
        {hidden ? '••••' : formatCompactCurrency(value)}
      </Typo>
    </View>
  );
}

function IncomeExpenseCard({
  data,
  mode,
  onModeChange,
  hidden,
}: {
  data: AnalyticsDashboard;
  mode: DetailMode;
  onModeChange: (mode: DetailMode) => void;
  hidden: boolean;
}) {
  const points = mode === 'weekly' ? data.series : data.monthlyHistory.map(monthToSeriesPoint);
  const max = Math.max(1, ...points.flatMap((point) => [point.income, point.expense]));

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <View>
          <Typo variant="bodyStrong">Ingresos vs Gastos</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Comparativo actual del mes
          </Typo>
        </View>
        <View style={styles.modeSwitch}>
          <ModeButton label="Semanal" selected={mode === 'weekly'} onPress={() => onModeChange('weekly')} />
          <ModeButton label="Mensual" selected={mode === 'monthly'} onPress={() => onModeChange('monthly')} />
        </View>
      </View>

      <View style={styles.legendRow}>
        <LegendDot color={colors.accent} label="Ingresos" />
        <LegendDot color={colors.primary} label="Gastos" />
      </View>

      <View style={styles.barChart}>
        {points.slice(-6).map((point) => (
          <View key={`${point.from}-${point.label}`} style={styles.barColumn}>
            <View style={styles.barPair}>
              <View style={[styles.bar, { height: Math.max(4, (point.income / max) * 112), backgroundColor: colors.accent }]} />
              <View style={[styles.bar, { height: Math.max(4, (point.expense / max) * 112), backgroundColor: colors.primary }]} />
            </View>
            <Typo variant="overline" color={colors.textSecondary} numberOfLines={1}>
              {point.label}
            </Typo>
          </View>
        ))}
      </View>
      <Typo variant="caption" color={colors.textSecondary} align="center">
        Total visible: {hidden ? '••••' : formatCompactCurrency(max)}
      </Typo>
    </Card>
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

function ModeButton({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  return (
    <Pressable onPress={onPress} style={[styles.modeButton, selected ? styles.modeButtonSelected : null]}>
      <Typo variant="overline" color={selected ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
    </Pressable>
  );
}

function LegendDot({ color, label }: { color: string; label: string }) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.legendDot, { backgroundColor: color }]} />
      <Typo variant="caption" color={colors.textSecondary}>
        {label}
      </Typo>
    </View>
  );
}

function BalanceEvolutionCard({ points, hidden }: { points: BalancePoint[]; hidden: boolean }) {
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
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <View>
          <Typo variant="bodyStrong">Evolución del saldo disponible</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Comportamiento del dinero y proyección
          </Typo>
        </View>
        <View style={styles.rightValue}>
          <Typo variant="bodyStrong" tabular>
            {hidden ? '••••' : formatCurrency(last.balance)}
          </Typo>
          <Typo variant="overline" color={colors.textSecondary}>
            cierre
          </Typo>
        </View>
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

      <View style={styles.legendRow}>
        <LegendDot color={colors.primary} label="Línea continua: saldo verificado" />
        <LegendDot color={colors.accent} label="Último punto: cierre" />
      </View>
    </Card>
  );
}

function CategoryCard({ items, spotlightCategoryId, hidden }: { items: CategoryTrend[]; spotlightCategoryId: string | null; hidden: boolean }) {
  const max = Math.max(1, ...items.map((item) => item.total));

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">Gastos por categoría</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {items.length} categorías
        </Typo>
      </View>
      <View style={styles.categoryStack}>
        {items.slice(0, 6).map((item) => (
          <View key={item.categoryId} style={styles.categoryRow}>
            <View style={[styles.categoryIcon, { backgroundColor: `${item.color}24` }]}>
              <Ionicons name={iconForCategory(item.icon)} size={16} color={item.color} />
            </View>
            <View style={styles.flex}>
              <View style={styles.cardTitleRow}>
                <Typo variant="caption" numberOfLines={1} style={styles.flex}>
                  {item.name}
                </Typo>
                <Typo variant="caption" tabular>
                  {hidden ? '••••' : formatCurrency(item.total)}
                </Typo>
              </View>
              <View style={styles.categoryTrack}>
                <View
                  style={[
                    styles.categoryFill,
                    { width: `${Math.max(4, (item.total / max) * 100)}%`, backgroundColor: item.color },
                  ]}
                />
              </View>
              <Typo variant="overline" color={item.changePercent !== null && item.changePercent > 0 ? colors.warning : colors.textSecondary}>
                {Math.round(item.percentage)}% · {item.count} mov.
                {item.categoryId === spotlightCategoryId ? ' · mayor cambio' : ''}
              </Typo>
            </View>
          </View>
        ))}
      </View>
    </Card>
  );
}

function SpendStructureCard({ recurring, hidden }: { recurring: RecurringPayment[]; hidden: boolean }) {
  const totalRecurring = recurring.reduce((sum, item) => sum + item.averageAmount, 0);

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">Estructura del gasto</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Compromisos fijos frente a decisiones diarias
        </Typo>
      </View>
      <View style={styles.twoColumns}>
        <StructureBox title="Fijos / recurrentes" value={totalRecurring} hint={`${recurring.length} detectados`} hidden={hidden} />
        <StructureBox title="Variables / otros" value={0} hint="resto del consumo" hidden={hidden} muted />
      </View>
      {recurring.length > 0 ? (
        <View style={styles.listCard}>
          {recurring.slice(0, 3).map((item, index) => (
            <View key={item.merchant} style={[styles.listRow, index > 0 ? styles.rowDivider : null]}>
              <View style={styles.smallIcon}>
                <Ionicons name="receipt-outline" size={15} color={colors.text} />
              </View>
              <View style={styles.flex}>
                <Typo variant="caption" numberOfLines={1}>
                  {item.merchant}
                </Typo>
                <Typo variant="overline" color={colors.textSecondary}>
                  {item.occurrencesLast6Months} veces en 6 meses
                </Typo>
              </View>
              <Typo variant="caption" tabular>
                {hidden ? '••••' : formatCurrency(item.averageAmount)}
              </Typo>
            </View>
          ))}
        </View>
      ) : (
        <Typo variant="caption" color={colors.textSecondary}>
          Aún no hay pagos recurrentes suficientes para separarlos del gasto variable.
        </Typo>
      )}
    </Card>
  );
}

function StructureBox({
  title,
  value,
  hint,
  hidden,
  muted = false,
}: {
  title: string;
  value: number;
  hint: string;
  hidden: boolean;
  muted?: boolean;
}) {
  return (
    <View style={[styles.structureBox, muted ? styles.structureMuted : null]}>
      <Typo variant="overline" color={colors.textSecondary}>
        {title}
      </Typo>
      <Typo variant="heading" tabular>
        {hidden ? '••••' : formatCurrency(value)}
      </Typo>
      <Typo variant="caption" color={colors.textSecondary}>
        {hint}
      </Typo>
    </View>
  );
}

function MerchantsAndPeaksCard({
  merchants,
  months,
  hidden,
}: {
  merchants: MerchantRanking[];
  months: MonthlyHistoryItem[];
  hidden: boolean;
}) {
  const max = Math.max(1, ...merchants.map((item) => item.total));

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">Top comercios y días pico</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Donde se concentra el consumo
        </Typo>
      </View>

      {merchants.slice(0, 5).map((merchant, index) => (
        <View key={merchant.merchant} style={styles.merchantRow}>
          <View style={styles.merchantLogo}>
            <Typo variant="overline">{merchant.merchant.slice(0, 2).toUpperCase()}</Typo>
          </View>
          <View style={styles.flex}>
            <Typo variant="caption" numberOfLines={1}>
              {merchant.merchant}
            </Typo>
            <View style={styles.merchantTrack}>
              <View style={[styles.merchantFill, { width: `${Math.max(5, (merchant.total / max) * 100)}%` }]} />
            </View>
          </View>
          <View style={styles.rightValue}>
            <Typo variant="caption" tabular>
              {hidden ? '••••' : formatCurrency(merchant.total)}
            </Typo>
            <Typo variant="overline" color={colors.textSecondary}>
              {index + 1}
            </Typo>
          </View>
        </View>
      ))}

      {months.length > 0 ? (
        <View style={styles.peakGrid}>
          {months.slice(-3).map((month) => (
            <View key={month.monthLabel} style={styles.peakChip}>
              <Typo variant="overline" color={colors.textSecondary}>
                {month.monthLabel}
              </Typo>
              <Typo variant="caption" tabular>
                {hidden ? '••••' : formatCompactCurrency(month.expense)}
              </Typo>
            </View>
          ))}
        </View>
      ) : null}
    </Card>
  );
}

function HabitPatternCard({ data, hidden }: { data: AnalyticsDashboard; hidden: boolean }) {
  const weekday = data.weekdayWeekend.weekdayAveragePerDay;
  const weekend = data.weekdayWeekend.weekendAveragePerDay;
  const total = Math.max(weekday + weekend, 1);
  const weekdayPercent = Math.round((weekday / total) * 100);

  return (
    <Card style={styles.largeCard}>
      <View style={styles.inline}>
        <View style={styles.habitIcon}>
          <Ionicons name="calendar-outline" size={18} color={colors.text} />
        </View>
        <Typo variant="bodyStrong">Patrón de hábitos</Typo>
      </View>
      <Typo variant="caption" color={colors.textSecondary}>
        Los fines de semana concentran el {100 - weekdayPercent}% de tus gastos variables, principalmente en salidas y entretenimiento.
      </Typo>
      <View style={styles.twoColumns}>
        <HabitMetric label="Lun - Vie" value={weekday} hidden={hidden} />
        <HabitMetric label="Sáb - Dom" value={weekend} hidden={hidden} />
      </View>
      <View style={styles.scoreTrack}>
        <View style={[styles.scoreFill, { width: `${weekdayPercent}%`, backgroundColor: colors.accentSecondary }]} />
      </View>
    </Card>
  );
}

function HabitMetric({ label, value, hidden }: { label: string; value: number; hidden: boolean }) {
  return (
    <View>
      <Typo variant="overline" color={colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant="caption" tabular>
        {hidden ? '••••' : `${formatCurrency(value)} prom.`}
      </Typo>
    </View>
  );
}

function MonthComparisonCard({ months, hidden }: { months: MonthlyHistoryItem[]; hidden: boolean }) {
  const current = months[months.length - 1]!;
  const previous = months[months.length - 2]!;
  const rows = [
    { label: 'Ingresos', current: current.income, previous: previous.income, goodWhenUp: true },
    { label: 'Gastos totales', current: current.expense, previous: previous.expense, goodWhenUp: false },
    { label: 'Ahorro neto', current: current.net, previous: previous.net, goodWhenUp: true },
  ];

  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">Este mes vs Mes anterior</Typo>
        <Ionicons name="analytics-outline" size={17} color={colors.textSecondary} />
      </View>
      {rows.map((row) => {
        const change = row.previous === 0 ? null : ((row.current - row.previous) / Math.abs(row.previous)) * 100;
        const positive = change === null ? true : row.goodWhenUp ? change >= 0 : change <= 0;
        return (
          <View key={row.label} style={styles.compareRow}>
            <View style={styles.flex}>
              <Typo variant="caption">{row.label}</Typo>
              <Typo variant="overline" color={colors.textSecondary}>
                {previous.monthLabel} a {current.monthLabel}
              </Typo>
            </View>
            <Typo variant="caption" tabular>
              {hidden ? '••••' : formatCompactCurrency(row.current - row.previous)}
            </Typo>
            <Badge label={change === null ? 'nuevo' : `${positive ? '+' : ''}${Math.round(change)}%`} tone={positive ? 'positive' : 'attention'} />
          </View>
        );
      })}
    </Card>
  );
}

function ConclusionsCard({ insights }: { insights: Insight[] }) {
  return (
    <Card style={styles.largeCard}>
      <View style={styles.cardTitleRow}>
        <Typo variant="bodyStrong">Conclusiones del mes</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {insights.length} detectadas
        </Typo>
      </View>
      {insights.slice(0, 3).map((insight) => (
        <View key={insight.code} style={styles.conclusionRow}>
          <View style={[styles.conclusionIcon, { backgroundColor: insight.severity === 'Attention' ? 'rgba(228,168,83,0.16)' : `${colors.accent}55` }]}>
            <Ionicons
              name={insight.severity === 'Attention' ? 'warning-outline' : 'sparkles-outline'}
              size={16}
              color={insight.severity === 'Attention' ? colors.warning : colors.text}
            />
          </View>
          <View style={styles.flex}>
            <Typo variant="caption" numberOfLines={1}>
              {insight.title}
            </Typo>
            <Typo variant="overline" color={colors.textSecondary} numberOfLines={2}>
              {insight.body}
            </Typo>
          </View>
        </View>
      ))}
    </Card>
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
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  statusDot: {
    width: 18,
    height: 18,
    borderRadius: 9,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatar: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  chips: {
    gap: spacing.sm,
    paddingRight: spacing.lg,
  },
  segmentChip: {
    minHeight: 34,
    maxWidth: 140,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  segmentChipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  loading: {
    gap: spacing.lg,
  },
  healthCard: {
    gap: spacing.md,
  },
  healthTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  scoreRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.md,
  },
  scoreText: {
    flex: 1,
    paddingBottom: spacing.sm,
  },
  scoreTrack: {
    height: 9,
    borderRadius: 5,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
  },
  scoreFill: {
    height: '100%',
    borderRadius: 5,
    backgroundColor: colors.accent,
  },
  kpiGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginTop: spacing.md,
  },
  kpiTile: {
    flexBasis: '47%',
    flexGrow: 1,
    minHeight: 118,
    gap: spacing.xs,
  },
  kpiTop: {
    minHeight: 22,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.xs,
  },
  largeCard: {
    gap: spacing.md,
    marginTop: spacing.md,
  },
  cardTitleRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  flowRibbon: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  flowMetric: {
    flex: 1,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.sm,
    gap: 2,
  },
  multiTrack: {
    height: 10,
    borderRadius: 5,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
    flexDirection: 'row',
  },
  trackPart: {
    height: '100%',
  },
  tipBox: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  modeSwitch: {
    flexDirection: 'row',
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.pill,
    padding: 3,
  },
  modeButton: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: 5,
  },
  modeButtonSelected: {
    backgroundColor: colors.surface,
  },
  legendRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
  },
  legendItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  legendDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
  barChart: {
    height: 150,
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  barColumn: {
    flex: 1,
    alignItems: 'center',
    gap: spacing.sm,
  },
  barPair: {
    height: 120,
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: 5,
  },
  bar: {
    width: 8,
    borderRadius: 4,
  },
  rightValue: {
    alignItems: 'flex-end',
    gap: 2,
  },
  lineChart: {
    height: 148,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    overflow: 'hidden',
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
  categoryStack: {
    gap: spacing.md,
  },
  categoryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  categoryIcon: {
    width: 36,
    height: 36,
    borderRadius: 18,
    alignItems: 'center',
    justifyContent: 'center',
  },
  categoryTrack: {
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
    marginTop: spacing.xs,
  },
  categoryFill: {
    height: '100%',
    borderRadius: 3,
  },
  twoColumns: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  structureBox: {
    flex: 1,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xs,
  },
  structureMuted: {
    opacity: 0.72,
  },
  listCard: {
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
  },
  listRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.md,
  },
  rowDivider: {
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  smallIcon: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  merchantRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  merchantLogo: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
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
  peakGrid: {
    flexDirection: 'row',
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
  peakChip: {
    flex: 1,
    alignItems: 'center',
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.sm,
  },
  habitIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  compareRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  conclusionRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surfaceSecondary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  conclusionIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
