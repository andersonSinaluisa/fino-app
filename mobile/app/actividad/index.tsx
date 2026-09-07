import { useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Card, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import { useSecurityActivity } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import type { SecurityEvent } from '../../types/api';

/**
 * Entregable 21 ("Auditoría"): the user's own security/account activity,
 * newest first -- inicios de sesión, bloqueos, cuentas agregadas o
 * eliminadas, exportaciones, etc. `label` already comes translated from the
 * backend (AuditActivityService.LabelFor), so this screen never duplicates
 * the action-to-sentence mapping; it only picks an icon from `action`.
 */
const ICONS: Record<string, keyof typeof Ionicons.glyphMap> = {
  'user.registered': 'person-add-outline',
  'user.logged_in': 'log-in-outline',
  'user.login_failed': 'alert-circle-outline',
  'user.account_locked': 'lock-closed-outline',
  'user.login_blocked': 'lock-closed-outline',
  'auth.token_reuse_detected': 'warning-outline',
  'user.logged_out': 'log-out-outline',
  'financial_account.created': 'wallet-outline',
  'financial_account.archived': 'archive-outline',
  'financial_account.deleted': 'trash-outline',
  'import.confirmed': 'cloud-upload-outline',
  'email_connection.connected': 'mail-outline',
  'email_connection.disconnected': 'mail-outline',
  'privacy.data_exported': 'download-outline',
  'privacy.transactions_deleted': 'trash-outline',
  'privacy.account_deletion_requested': 'trash-outline',
};

/** Events worth calling out with a warning tint instead of the neutral one. */
const ATTENTION_ACTIONS = new Set([
  'user.login_failed',
  'user.account_locked',
  'user.login_blocked',
  'auth.token_reuse_detected',
]);

export default function SecurityActivityScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage, refetch, isRefetching } =
    useSecurityActivity();

  const items = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data]);

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Actividad de la cuenta</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          Inicios de sesión, cambios y exportaciones -- más reciente primero.
        </Typo>
      </View>

      {isLoading ? (
        <View style={styles.loading}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <ActivityRow event={item} />}
          contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 40 }]}
          showsVerticalScrollIndicator={false}
          refreshing={isRefetching}
          onRefresh={() => void refetch()}
          onEndReachedThreshold={0.4}
          onEndReached={() => {
            if (hasNextPage && !isFetchingNextPage) {
              void fetchNextPage();
            }
          }}
          ListEmptyComponent={
            <EmptyState icon="time-outline" title="Sin actividad todavía" />
          }
          ListFooterComponent={
            isFetchingNextPage ? <ActivityIndicator style={styles.footer} color={colors.textSecondary} /> : null
          }
        />
      )}
    </View>
  );
}

function ActivityRow({ event }: { event: SecurityEvent }) {
  const attention = ATTENTION_ACTIONS.has(event.action);

  return (
    <Card style={styles.row}>
      <View style={[styles.icon, attention ? styles.iconAttention : null]}>
        <Ionicons
          name={ICONS[event.action] ?? 'ellipse-outline'}
          size={17}
          color={attention ? colors.danger : colors.text}
        />
      </View>

      <View style={styles.flex}>
        <Typo variant="body">{event.label}</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {formatRelativeTime(event.createdAt)}
        </Typo>
      </View>
    </Card>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  flex: { flex: 1 },
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
  loading: {
    paddingHorizontal: spacing.xl,
    gap: spacing.lg,
  },
  list: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  icon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconAttention: {
    backgroundColor: 'rgba(216, 102, 91, 0.14)',
  },
  footer: {
    marginVertical: spacing.lg,
  },
});
