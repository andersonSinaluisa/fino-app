import { useMemo } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { CategoryBreakdown } from '../../components/home/CategoryBreakdown';
import { InsightCard } from '../../components/home/InsightCard';
import { useSummary } from '../../hooks/queries';
import { estimateAvailableMoney, monthRangeLabel } from '../../utils/planCalculations';
import { formatCurrency } from '../../utils/format';

export default function PlanScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useSummary();
  const estimate = useMemo(() => (data ? estimateAvailableMoney(data) : null), [data]);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Estimación inteligente de Nexo" tone="accent" />
        <Typo variant="title">Tu Plan</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Finanzas calculadas al cierre del mes con datos reales de tus cuentas.
        </Typo>
      </View>

      {isLoading || !data || !estimate ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : data.accountCount === 0 ? (
        <EmptyState
          icon="wallet-outline"
          title="Sin datos para planificar"
          body="Agrega una cuenta o importa movimientos para construir tu plan."
          actionLabel="Agregar cuenta"
          onAction={() => router.push('/cuentas/agregar')}
        />
      ) : (
        <>
          <Card style={styles.hero}>
            <View style={styles.heroTop}>
              <Typo variant="overline" color={colors.textSecondary}>
                DISPONIBLE REAL HASTA FIN DE MES
              </Typo>
              <Badge label="Libre de culpas" tone="positive" />
            </View>
            <Typo variant="display" tabular>
              {formatCurrency(estimate.availableUntilMonthEnd)}
            </Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              Quedan {estimate.daysRemaining} día{estimate.daysRemaining === 1 ? '' : 's'} en el ciclo {monthRangeLabel()}.
            </Typo>

            <View style={styles.planLine}>
              <Typo variant="caption" color={colors.textSecondary}>
                Ritmo sugerido
              </Typo>
              <Typo variant="caption" color={colors.text} tabular>
                {formatCurrency(estimate.recommendedDailySpend)} / día
              </Typo>
            </View>
            <View style={styles.track}>
              <View
                style={[
                  styles.fill,
                  { width: `${Math.min(100, Math.max(6, (data.month.expense / Math.max(estimate.projectedMonthlyExpense, 1)) * 100))}%` },
                ]}
              />
            </View>
            <Button label="Ver desglose del cálculo" variant="secondary" onPress={() => router.push('/dinero-disponible')} />
          </Card>

          <View style={styles.section}>
            <SectionHeader title="Herramientas activas" />
            <View style={styles.grid}>
              <ToolCard icon="pie-chart-outline" label="Categorías" value={`${data.categoryBreakdown.length} activas`} />
              <ToolCard icon="wallet-outline" label="Cuentas" value={`${data.accountCount} vinculadas`} />
              <ToolCard icon="trending-up-outline" label="Proyección" value={`${formatCurrency(data.month.net, { signed: true })} neto`} />
              <ToolCard icon="sparkles-outline" label="Ideas" value={`${data.insights.length} insight${data.insights.length === 1 ? '' : 's'}`} />
            </View>
          </View>

          {data.categoryBreakdown.length > 0 ? (
            <View style={styles.section}>
              <SectionHeader title="Presupuesto observado" actionLabel="Análisis" onAction={() => router.push('/analisis-gastos')} />
              <CategoryBreakdown items={data.categoryBreakdown} limit={4} />
            </View>
          ) : null}

          {data.insights.length > 0 ? (
            <View style={styles.section}>
              <SectionHeader title="Cómo cierra tu mes" />
              <InsightCard insight={data.insights[0]!} style={styles.fullWidthCard} />
            </View>
          ) : null}

          <View style={styles.section}>
            <Button label="Ajustar parámetros del plan" onPress={() => router.push('/dinero-disponible')} />
          </View>
        </>
      )}
    </Screen>
  );
}

function ToolCard({ icon, label, value }: { icon: keyof typeof Ionicons.glyphMap; label: string; value: string }) {
  return (
    <Card style={styles.tool}>
      <View style={styles.toolIcon}>
        <Ionicons name={icon} size={18} color={colors.text} />
      </View>
      <Typo variant="bodyStrong">{label}</Typo>
      <Typo variant="caption" color={colors.textSecondary}>
        {value}
      </Typo>
    </Card>
  );
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
  stack: {
    gap: spacing.md,
  },
  hero: {
    gap: spacing.md,
  },
  heroTop: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  planLine: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: spacing.md,
    marginTop: spacing.sm,
  },
  track: {
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.surfaceSecondary,
    overflow: 'hidden',
  },
  fill: {
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.accent,
  },
  section: {
    marginTop: spacing.xxl,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  tool: {
    width: '48%',
    gap: spacing.sm,
  },
  toolIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  fullWidthCard: {
    width: '100%',
  },
});
