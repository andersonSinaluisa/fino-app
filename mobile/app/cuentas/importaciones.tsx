import { useMemo } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Badge, Card, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import { useCancelImport, useImportHistory } from '../../hooks/queries';
import { formatRelativeTime } from '../../utils/format';
import type { ImportSummary } from '../../types/api';

const STATUS_LABEL: Record<ImportSummary['status'], string> = {
  Received: 'Procesando',
  PreviewReady: 'Pendiente de confirmar',
  Completed: 'Importado',
  Failed: 'Error',
  Cancelled: 'Cancelado',
};

const STATUS_TONE: Record<ImportSummary['status'], 'neutral' | 'positive' | 'attention' | 'danger'> = {
  Received: 'attention',
  PreviewReady: 'attention',
  Completed: 'positive',
  Failed: 'danger',
  Cancelled: 'neutral',
};

/**
 * Entregable 8: every import a user has ever started, newest first, whatever
 * account it belongs to. A pending row (Received/PreviewReady) can be closed
 * from here too -- the point of this screen is that nothing stays orphaned
 * just because someone left the upload flow without confirming.
 */
export default function ImportHistoryScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage, refetch, isRefetching } =
    useImportHistory();
  const cancelImport = useCancelImport();

  const items = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data]);
  const total = data?.pages[0]?.totalCount ?? 0;

  const confirmCancel = (item: ImportSummary) => {
    Alert.alert(
      'Cancelar importación',
      `Se descartará el preview de "${item.fileName}". No se guardará ningún movimiento de este archivo.`,
      [
        { text: 'Volver', style: 'cancel' },
        {
          text: 'Cancelar importación',
          style: 'destructive',
          onPress: () => cancelImport.mutate(item.importId),
        },
      ],
    );
  };

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="close" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Cerrar
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Historial de importaciones</Typo>
        {total > 0 ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {total === 1 ? '1 importación' : `${total} importaciones`}
          </Typo>
        ) : null}
      </View>

      {isLoading ? (
        <View style={styles.loading}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(item) => item.importId}
          renderItem={({ item }) => (
            <ImportHistoryRow item={item} onCancel={() => confirmCancel(item)} />
          )}
          contentContainerStyle={[styles.list, { paddingBottom: insets.bottom + 120 }]}
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
            <EmptyState
              icon="cloud-upload-outline"
              title="Sin importaciones todavía"
              body="Cuando subas un estado de cuenta, aparecerá aquí con su resultado."
            />
          }
          ListFooterComponent={
            isFetchingNextPage ? <ActivityIndicator style={styles.footer} color={colors.textSecondary} /> : null
          }
        />
      )}
    </View>
  );
}

function ImportHistoryRow({ item, onCancel }: { item: ImportSummary; onCancel: () => void }) {
  const pending = item.status === 'Received' || item.status === 'PreviewReady';
  const problems = item.duplicateRows + item.probableDuplicateRows;

  return (
    <Card style={styles.row}>
      <View style={styles.rowTop}>
        <View style={styles.flex}>
          <Typo variant="bodyStrong" numberOfLines={1}>
            {item.fileName}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {item.accountAlias} · {formatRelativeTime(item.createdAt)}
          </Typo>
        </View>
        <Badge label={STATUS_LABEL[item.status]} tone={STATUS_TONE[item.status]} />
      </View>

      {item.status === 'Failed' ? null : (
        <View style={styles.stats}>
          <Stat label="Nuevos" value={item.status === 'Completed' ? item.importedCount : item.newRows} />
          <Stat label="Duplicados" value={problems} />
          {item.invalidRows > 0 ? <Stat label="Errores" value={item.invalidRows} tone={colors.danger} /> : null}
        </View>
      )}

      {pending ? (
        <Pressable onPress={onCancel} style={styles.cancelAction} hitSlop={8}>
          <Typo variant="caption" color={colors.danger}>
            Cancelar importación
          </Typo>
        </Pressable>
      ) : null}
    </Card>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <View style={styles.stat}>
      <Typo variant="bodyStrong" tabular color={tone ?? colors.text}>
        {value}
      </Typo>
      <Typo variant="overline" color={colors.textSecondary}>
        {label}
      </Typo>
    </View>
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
    gap: spacing.md,
    marginBottom: spacing.md,
  },
  rowTop: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  stats: {
    flexDirection: 'row',
    gap: spacing.xl,
  },
  stat: {
    gap: 2,
  },
  cancelAction: {
    alignSelf: 'flex-start',
    borderRadius: radius.md,
  },
  footer: {
    marginVertical: spacing.lg,
  },
});
