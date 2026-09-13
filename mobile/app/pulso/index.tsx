import { FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Card, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import { usePulses } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import type { Pulse } from '../../types/api';

const iconFor: Record<Pulse['type'], keyof typeof Ionicons.glyphMap> = {
  SpendingPace: 'speedometer-outline',
  UnusualExpense: 'alert-circle-outline',
  BalanceChange: 'swap-vertical-outline',
  UpcomingCommitment: 'calendar-outline',
  MonthEndProjection: 'trending-up-outline',
  SpendingImprovement: 'trophy-outline',
  CategorySpike: 'flame-outline',
  NewIncome: 'cash-outline',
  DailyClose: 'moon-outline',
  AccountOutdated: 'refresh-outline',
};

const accentFor: Record<Pulse['severity'], string> = {
  Positive: colors.accentSecondary,
  Attention: colors.warning,
  Risk: colors.danger,
  Neutral: colors.textSecondary,
};

/**
 * PULSO FASE 2: "Actividad de FINO" -- todo lo que PulseEngine ha detectado
 * hasta ahora, más relevante primero (el mismo orden que ya trae usePulses,
 * el que también usa la card de Home). Solo lectura, igual que la card: la
 * evaluación corre en el servidor (PulseEvaluationWorker).
 */
export default function PulsoHistoryScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();

  const { data: pulses, isLoading, refetch, isRefetching } = usePulses();

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Actividad de FINO</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Lo que hemos notado en tus finanzas, de lo más a lo menos relevante.
        </Typo>
      </View>

      <FlatList
        data={pulses ?? []}
        keyExtractor={(item) => item.id}
        contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 120 }]}
        showsVerticalScrollIndicator={false}
        refreshing={isRefetching}
        onRefresh={() => void refetch()}
        renderItem={({ item }) => (
          <PulseRow item={item} onPress={() => router.push(`/pulso/${item.id}`)} />
        )}
        ListEmptyComponent={
          isLoading ? (
            <View style={styles.loading}>
              <SkeletonCard />
              <SkeletonCard />
            </View>
          ) : (
            <EmptyState
              icon="sparkles-outline"
              title="Todavía no hay nada que contar"
              body="Cuando notemos algo relevante en tus movimientos o cuentas, aparecerá aquí."
            />
          )
        }
      />
    </View>
  );
}

function PulseRow({ item, onPress }: { item: Pulse; onPress: () => void }) {
  const accent = accentFor[item.severity];

  return (
    <Pressable onPress={onPress}>
      <Card style={styles.row}>
        <View style={[styles.icon, { backgroundColor: `${accent}33` }]}>
          <Ionicons name={iconFor[item.type] ?? 'sparkles-outline'} size={16} color={colors.text} />
        </View>
        <View style={styles.rowBody}>
          <Typo variant="bodyStrong" numberOfLines={1}>
            {item.title}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary} numberOfLines={2}>
            {item.body}
          </Typo>
        </View>
        <Typo variant="overline" color={colors.textSecondary}>
          {formatRelativeTime(item.occurredAt)}
        </Typo>
      </Card>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginHorizontal: spacing.xl,
    marginBottom: spacing.lg,
  },
  header: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.lg,
    gap: 2,
  },
  list: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  loading: {
    gap: spacing.lg,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  icon: {
    width: 34,
    height: 34,
    borderRadius: radius.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  rowBody: {
    flex: 1,
    gap: 2,
  },
});
