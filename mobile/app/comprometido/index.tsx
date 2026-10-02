import { useEffect, useRef, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { useCommittedMoney } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { AnalyticsEvent, AnalyticsSource, track } from '../../services/analytics';
import { formatCurrency, formatShortDate } from '../../utils/format';
import { formatDueDate } from '../../utils/creditCards';
import type { CommittedItem, CommittedSource } from '../../types/api';

const SOURCE_ICON: Record<CommittedSource['type'], keyof typeof Ionicons.glyphMap> = {
  reserved_budget: 'lock-closed-outline',
  upcoming_payment: 'calendar-outline',
  credit_card: 'card-outline',
};

/** Tarjetas: se pliega (una línea por tarjeta) y se expande al tocarla. */
const COLLAPSIBLE: ReadonlySet<CommittedSource['type']> = new Set(['credit_card']);

/**
 * "¿De dónde salió mi Comprometido?". Cada peso tiene su origen: presupuestos
 * con reserva (tocables, llevan a su detalle) y próximos pagos detectados. Si
 * un pago ya está dentro de un presupuesto reservado se dice explícitamente
 * ("incluido en…") en vez de desaparecer en silencio. Todo viene de
 * GET /finance/committed; aquí no se suma nada.
 */
export default function CommittedScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ source?: string }>();
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const { data, isLoading, isError, refetch, isRefetching } = useCommittedMoney();
  const [expanded, setExpanded] = useState<ReadonlySet<CommittedSource['type']>>(new Set());
  const toggle = (type: CommittedSource['type']) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(type)) {
        next.delete(type);
      } else {
        next.add(type);
      }
      return next;
    });

  const tracked = useRef(false);
  useEffect(() => {
    if (!data || tracked.current) {
      return;
    }
    tracked.current = true;
    track(AnalyticsEvent.CommittedBreakdownOpened, {
      source: params.source ?? AnalyticsSource.Home,
      hasData: data.committed > 0,
    });
  }, [data, params.source]);

  const money = (value: number) => formatCurrency(value, { hidden });

  return (
    <Screen refreshing={isRefetching} onRefresh={() => void refetch()}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back} accessibilityRole="button" accessibilityLabel="Volver">
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      {isLoading || !data ? (
        isError ? (
          <EmptyState icon="cloud-offline-outline" title="No pudimos calcular tu Comprometido" actionLabel="Reintentar" onAction={() => void refetch()} />
        ) : (
          <View style={styles.stack}>
            <SkeletonCard />
            <SkeletonCard />
          </View>
        )
      ) : (
        <>
          <View style={styles.header}>
            <Typo variant="overline" color={colors.textSecondary}>
              DINERO COMPROMETIDO
            </Typo>
            <Typo variant="display" tabular>
              {money(data.committed)}
            </Typo>
            <Typo variant="body" color={colors.textSecondary}>
              Dinero que ya tiene un destino.
            </Typo>
          </View>

          {data.isOvercommitted ? (
            <View style={styles.alert} accessibilityRole="alert">
              <Ionicons name="warning-outline" size={18} color={colors.danger} />
              <Typo variant="body" style={styles.flex}>
                {hidden
                  ? 'Tienes más comprometido de lo que tienes disponible.'
                  : `Tienes ${money(data.overcommitted)} más comprometidos de lo que tienes disponible.`}
              </Typo>
            </View>
          ) : null}

          {data.sources.length === 0 ? (
            <Card style={styles.stack}>
              <Typo variant="subheading">Nada comprometido por ahora</Typo>
              <Typo variant="body" color={colors.textSecondary}>
                Cuando reservas dinero en un presupuesto (alquiler, servicios), activas «Reservar el próximo pago» en una tarjeta o Fino detecta un pago que se repite cada mes y todavía no llega, aparece aquí.
              </Typo>
              <Button
                label="Crear un presupuesto"
                variant="secondary"
                onPress={() => router.push('/presupuestos')}
              />
            </Card>
          ) : (
            <View style={styles.stack}>
              {data.sources.map((source) => {
                const collapsible = COLLAPSIBLE.has(source.type);
                const open = !collapsible || expanded.has(source.type);
                return (
                  <Card key={source.type} style={styles.stack}>
                    <Pressable
                      disabled={!collapsible}
                      onPress={() => toggle(source.type)}
                      accessibilityRole={collapsible ? 'button' : undefined}
                      accessibilityState={collapsible ? { expanded: open } : undefined}
                      accessibilityHint={collapsible ? 'Muestra cada tarjeta y su fecha de pago' : undefined}
                      style={styles.rowBetween}
                    >
                      <View style={styles.inline}>
                        <Ionicons name={SOURCE_ICON[source.type]} size={18} color={colors.text} />
                        <Typo variant="subheading">{source.label}</Typo>
                      </View>
                      <View style={styles.inline}>
                        <Typo variant="subheading" tabular>
                          {money(source.amount)}
                        </Typo>
                        {collapsible ? (
                          <Ionicons name={open ? 'chevron-up' : 'chevron-down'} size={16} color={colors.textSecondary} />
                        ) : null}
                      </View>
                    </Pressable>
                    <Typo variant="caption" color={colors.textSecondary}>
                      {source.description}
                    </Typo>
                    {open ? (
                      <View>
                        {source.items.map((item, index) => (
                          <View key={`${item.label}-${index}`}>
                            {index > 0 ? <View style={styles.divider} /> : null}
                            <ItemRow
                              item={item}
                              hidden={hidden}
                              onPress={
                                item.budgetId
                                  ? () =>
                                      router.push({
                                        pathname: '/presupuestos/[id]',
                                        params: { id: item.budgetId!, source: AnalyticsSource.Committed },
                                      })
                                  : item.creditCardId
                                    ? () =>
                                        router.push({
                                          pathname: '/tarjetas/[id]',
                                          params: { id: item.creditCardId!, source: AnalyticsSource.Committed },
                                        })
                                    : undefined
                              }
                            />
                          </View>
                        ))}
                      </View>
                    ) : null}
                  </Card>
                );
              })}

              <View style={[styles.rowBetween, styles.total]}>
                <Typo variant="bodyStrong" color={colors.onPrimary}>
                  TOTAL
                </Typo>
                <Typo variant="heading" color={colors.onPrimary} tabular>
                  {money(data.committed)}
                </Typo>
              </View>
            </View>
          )}

          <Card tone="secondary" style={styles.equation}>
            <EquationRow label="Tu dinero" value={money(data.currentMoney)} />
            <EquationRow label="Comprometido" value={`−${money(data.committed)}`} />
            <View style={styles.divider} />
            <EquationRow label="Disponible" value={money(data.available)} strong />
          </Card>
        </>
      )}
    </Screen>
  );
}

function ItemRow({ item, hidden, onPress }: { item: CommittedItem; hidden: boolean; onPress?: () => void }) {
  const covered = item.coveredByBudgetName !== null;
  const partlyCovered = covered && item.amount > 0;
  const detail = covered
    ? partlyCovered
      ? `Una parte ya está en tu presupuesto ${item.coveredByBudgetName}`
      : `Incluido en tu presupuesto ${item.coveredByBudgetName}`
    : item.creditCardId && item.expectedDate
      ? `Vence ${formatDueDate(item.expectedDate).toLowerCase()}`
      : item.expectedDate
        ? `Esperado cerca del ${formatShortDate(`${item.expectedDate}T12:00:00`)}`
        : null;

  const content = (
    <View style={styles.itemRow}>
      <View style={styles.flex}>
        <Typo variant="body" numberOfLines={1}>
          {item.label}
        </Typo>
        {detail ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {detail}
          </Typo>
        ) : null}
      </View>
      <Typo variant="bodyStrong" tabular color={covered && !partlyCovered ? colors.textSecondary : colors.text}>
        {formatCurrency(item.amount, { hidden })}
      </Typo>
      {onPress ? <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} /> : null}
    </View>
  );

  return onPress ? (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityHint={item.creditCardId ? 'Abre la tarjeta' : 'Abre el presupuesto'}>
      {content}
    </Pressable>
  ) : (
    content
  );
}

function EquationRow({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.rowBetween}>
      <Typo variant={strong ? 'bodyStrong' : 'body'} color={strong ? colors.text : colors.textSecondary}>
        {label}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular>
        {value}
      </Typo>
    </View>
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
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  alert: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: 'rgba(216, 102, 91, 0.10)',
    borderRadius: radius.md,
    padding: spacing.md,
    marginBottom: spacing.lg,
  },
  stack: {
    gap: spacing.md,
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
  itemRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingVertical: spacing.md,
  },
  flex: {
    flex: 1,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
  total: {
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    padding: spacing.lg,
  },
  equation: {
    gap: spacing.sm,
    marginTop: spacing.xl,
  },
});
