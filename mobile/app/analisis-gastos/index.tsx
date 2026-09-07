import { Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Badge, Button, EmptyState, Screen, SectionHeader, SkeletonCard, Typo } from '../../components/ui';
import { CategoryBreakdown } from '../../components/home/CategoryBreakdown';
import { InsightCard } from '../../components/home/InsightCard';
import { useSummary } from '../../hooks/queries';
import { formatCurrency, formatMonthComparison } from '../../utils/format';

export default function SpendingAnalysisScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useSummary();
  const comparison = data ? formatMonthComparison(data.monthComparison.expenseChangePercent) : null;

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Análisis de gastos" tone="accent" />
        <Typo variant="title">Así va tu dinero</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Categorías e ideas calculadas desde tus movimientos confirmados.
        </Typo>
      </View>

      {isLoading || !data ? (
        <View style={styles.stack}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : data.categoryBreakdown.length === 0 ? (
        <EmptyState
          icon="pie-chart-outline"
          title="Aún no hay suficiente información"
          body="Importa movimientos para ver el desglose por categoría."
          actionLabel="Ir a cuentas"
          onAction={() => router.push('/(tabs)/cuentas')}
        />
      ) : (
        <>
          <View style={styles.summary}>
            <Typo variant="caption" color={colors.textSecondary}>
              Este mes gastaste
            </Typo>
            <Typo variant="display" tabular>
              {formatCurrency(data.month.expense)}
            </Typo>
            {comparison ? (
              <Typo variant="body" color={colors.textSecondary}>
                Gastaste {comparison.label}.
              </Typo>
            ) : null}
          </View>

          <View style={styles.section}>
            <SectionHeader title="En qué se fue tu dinero" />
            <CategoryBreakdown items={data.categoryBreakdown} />
          </View>

          {data.insights.length > 0 ? (
            <View style={styles.section}>
              <SectionHeader title="Observaciones de tu mes" />
              <View style={styles.stack}>
                {data.insights.slice(0, 4).map((insight) => (
                  <InsightCard key={insight.code} insight={insight} style={styles.fullWidthCard} />
                ))}
              </View>
            </View>
          ) : null}

          <View style={styles.section}>
            <Button label="Crear alerta amable" onPress={() => router.push('/alertas-financieras')} />
          </View>
        </>
      )}
    </Screen>
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
  summary: {
    gap: spacing.xs,
  },
  section: {
    marginTop: spacing.xxl,
  },
  stack: {
    gap: spacing.md,
  },
  fullWidthCard: {
    width: '100%',
  },
});
