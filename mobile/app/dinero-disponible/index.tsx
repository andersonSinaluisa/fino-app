import { useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Badge, Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useSummary } from '../../hooks/queries';
import { estimateAvailableMoney, monthRangeLabel } from '../../utils/planCalculations';
import { formatCurrency } from '../../utils/format';
import { AnalyticsEvent, AnalyticsSource, track } from '../../services/analytics';

export default function AvailableMoneyScreen() {
  const router = useRouter();
  const { data, isLoading, refetch, isRefetching } = useSummary();
  const [simulationText, setSimulationText] = useState('');

  const estimate = useMemo(() => (data ? estimateAvailableMoney(data) : null), [data]);
  const simulatedSpend = Number(simulationText.replace(',', '.'));
  const simulatedAvailable = estimate && Number.isFinite(simulatedSpend)
    ? Math.max(estimate.availableUntilMonthEnd - simulatedSpend, 0)
    : null;

  // §16: adopción de la proyección. `hasData` distingue "la abrió y vio algo"
  // de "la abrió y estaba vacía", que son dos cosas muy distintas para
  // decidir si la función vale la pena. El importe estimado no sale.
  const hasEstimate = estimate !== null;

  useEffect(() => {
    track(AnalyticsEvent.ProjectionViewed, {
      source: AnalyticsSource.Home,
      hasData: hasEstimate,
    });
  }, [hasEstimate]);

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Badge label="Estimación inteligente" tone="accent" />
        <Typo variant="title">Dinero disponible</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Calculado con tu saldo real y el ritmo de gastos que ya existe en Nexo.
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
          title="Agrega una cuenta"
          body="Necesitamos al menos una cuenta para estimar cuánto puedes usar."
          actionLabel="Agregar cuenta"
          onAction={() => router.push('/cuentas/agregar')}
        />
      ) : (
        <>
          <Card tone="accent" style={styles.hero}>
            <View style={styles.heroTop}>
              <Badge label="Libre de culpas" tone="positive" />
              <Typo variant="caption" color={colors.textSecondary}>
                En vivo
              </Typo>
            </View>
            <Typo variant="display" tabular>
              {formatCurrency(estimate.availableUntilMonthEnd)}
            </Typo>
            <Typo variant="body" color={colors.textSecondary}>
              Disponible estimado hasta fin de mes, después de proyectar tu ritmo actual de gastos.
            </Typo>

            <View style={styles.dailyBox}>
              <View style={styles.rowBetween}>
                <View style={styles.inline}>
                  <Ionicons name="speedometer-outline" size={16} color={colors.text} />
                  <Typo variant="bodyStrong">Ritmo seguro sugerido</Typo>
                </View>
                <Typo variant="bodyStrong" tabular>
                  {formatCurrency(estimate.recommendedDailySpend)} / día
                </Typo>
              </View>
              <Typo variant="caption" color={colors.textSecondary}>
                Corte en {estimate.daysRemaining} día{estimate.daysRemaining === 1 ? '' : 's'}.
              </Typo>
            </View>
          </Card>

          <View style={styles.sectionHeader}>
            <Typo variant="heading">¿De dónde sale?</Typo>
            <Typo variant="caption" color={colors.textSecondary}>
              {monthRangeLabel()}
            </Typo>
          </View>

          <Card style={styles.stack}>
            <BreakdownRow
              icon="wallet-outline"
              label="Saldo total en cuentas"
              hint={`${data.accountCount} cuenta${data.accountCount === 1 ? '' : 's'} activa${data.accountCount === 1 ? '' : 's'}`}
              amount={data.totalBalance}
              positive
            />
            <BreakdownRow
              icon="receipt-outline"
              label="Gasto proyectado restante"
              hint={`Ritmo actual: ${formatCurrency(data.month.expense)} gastados`}
              amount={-estimate.projectedRemainingExpense}
            />
            <View style={styles.totalRow}>
              <View style={styles.equalIcon}>
                <Ionicons name="reorder-two-outline" size={18} color={colors.onAccent} />
              </View>
              <Typo variant="bodyStrong" style={styles.flex}>
                Disponible real estimado
              </Typo>
              <Typo variant="heading" color={colors.accent} tabular>
                {formatCurrency(estimate.availableUntilMonthEnd)}
              </Typo>
            </View>
          </Card>

          <Card tone="secondary" style={styles.stack}>
            <View style={styles.inline}>
              <Ionicons name="calculator-outline" size={18} color={colors.text} />
              <Typo variant="subheading">Simulador de compra inmediata</Typo>
            </View>
            <View style={styles.simulatorRow}>
              <TextInput
                value={simulationText}
                onChangeText={setSimulationText}
                placeholder="Monto a gastar"
                placeholderTextColor={colors.textSecondary}
                keyboardType="decimal-pad"
                style={styles.simulatorInput}
              />
              <Button label="Probar" compact fullWidth={false} onPress={() => undefined} />
            </View>
            {simulatedAvailable !== null ? (
              <Typo variant="caption" color={colors.textSecondary}>
                Después de ese gasto quedarían {formatCurrency(simulatedAvailable)} disponibles.
              </Typo>
            ) : null}
          </Card>
        </>
      )}
    </Screen>
  );
}

function BreakdownRow({
  icon,
  label,
  hint,
  amount,
  positive = false,
}: {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  hint: string;
  amount: number;
  positive?: boolean;
}) {
  return (
    <View style={styles.breakdownRow}>
      <View style={styles.rowIcon}>
        <Ionicons name={icon} size={17} color={positive ? colors.success : colors.danger} />
      </View>
      <View style={styles.flex}>
        <Typo variant="bodyStrong">{label}</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {hint}
        </Typo>
      </View>
      <Typo variant="bodyStrong" color={positive ? colors.success : colors.danger} tabular>
        {formatCurrency(amount, { signed: true })}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
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
  hero: {
    gap: spacing.md,
  },
  heroTop: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  dailyBox: {
    marginTop: spacing.sm,
    backgroundColor: 'rgba(255, 255, 255, 0.55)',
    borderRadius: radius.md,
    padding: spacing.md,
    gap: spacing.xs,
  },
  rowBetween: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  inline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    marginTop: spacing.xxl,
    marginBottom: spacing.md,
    gap: spacing.md,
  },
  stack: {
    gap: spacing.md,
  },
  breakdownRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  rowIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  totalRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  equalIcon: {
    width: 34,
    height: 34,
    borderRadius: 17,
    backgroundColor: colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
  },
  simulatorRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  simulatorInput: {
    flex: 1,
    minHeight: 42,
    borderRadius: radius.pill,
    backgroundColor: colors.surface,
    paddingHorizontal: spacing.lg,
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
  },
});
