import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, ScrollView, StyleSheet, TextInput, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing, typography } from '../../theme';
import { Chip, EmptyState, SkeletonCard, Typo } from '../../components/ui';
import { DayGroupSection } from '../../components/transactions/DayGroup';
import { useAccounts, useCategories, useTransactions } from '../../hooks/queries';
import { usePreferencesStore } from '../../store/preferencesStore';
import { groupByDay } from '../../utils/format';
import type { TransactionDirection } from '../../types/api';

type DirectionFilter = 'all' | TransactionDirection;

export default function TransactionsScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const hidden = usePreferencesStore((state) => state.amountsHidden);

  const [search, setSearch] = useState('');
  const [direction, setDirection] = useState<DirectionFilter>('all');
  const [accountId, setAccountId] = useState<string | undefined>();
  const [categoryId, setCategoryId] = useState<string | undefined>();

  const { data: accounts } = useAccounts();
  const { data: categories } = useCategories();

  const query = useMemo(
    () => ({
      search: search.trim().length >= 2 ? search.trim() : undefined,
      direction: direction === 'all' ? undefined : direction,
      accountId,
      categoryId,
    }),
    [search, direction, accountId, categoryId],
  );

  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage, refetch, isRefetching } =
    useTransactions(query);

  const items = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data]);
  const groups = useMemo(() => groupByDay(items), [items]);
  const total = data?.pages[0]?.totalCount ?? 0;

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.sm }]}>
      <View style={styles.header}>
        <Typo variant="title">Movimientos</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {total === 1 ? '1 movimiento' : `${total} movimientos`}
        </Typo>
      </View>

      <View style={styles.searchRow}>
        <Ionicons name="search" size={17} color={colors.textSecondary} />
        <TextInput
          value={search}
          onChangeText={setSearch}
          placeholder="Buscar comercio o descripción"
          placeholderTextColor={colors.textSecondary}
          style={styles.searchInput}
          returnKeyType="search"
          autoCorrect={false}
        />
        {search.length > 0 ? (
          <Ionicons name="close-circle" size={17} color={colors.textSecondary} onPress={() => setSearch('')} />
        ) : null}
      </View>

      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={styles.filters}
        style={styles.filtersRow}
      >
        <Chip label="Todos" selected={direction === 'all' && !accountId && !categoryId} onPress={() => {
          setDirection('all');
          setAccountId(undefined);
          setCategoryId(undefined);
        }} />
        <Chip label="Ingresos" selected={direction === 'Income'} onPress={() => setDirection(direction === 'Income' ? 'all' : 'Income')} />
        <Chip label="Gastos" selected={direction === 'Expense'} onPress={() => setDirection(direction === 'Expense' ? 'all' : 'Expense')} />

        {(accounts ?? []).map((account) => (
          <Chip
            key={account.id}
            label={account.alias}
            selected={accountId === account.id}
            onPress={() => setAccountId(accountId === account.id ? undefined : account.id)}
          />
        ))}

        {(categories ?? []).map((category) => (
          <Chip
            key={category.id}
            label={category.name}
            selected={categoryId === category.id}
            onPress={() => setCategoryId(categoryId === category.id ? undefined : category.id)}
          />
        ))}
      </ScrollView>

      {isLoading ? (
        <View style={styles.loading}>
          <SkeletonCard />
          <SkeletonCard />
        </View>
      ) : (
        <FlatList
          data={groups}
          keyExtractor={(group) => group.key}
          renderItem={({ item }) => (
            <DayGroupSection
              group={item}
              hidden={hidden}
              onSelect={(transaction) => router.push(`/movimiento/${transaction.id}`)}
            />
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
              icon="receipt-outline"
              title="Sin movimientos"
              body="Cambia los filtros o importa un estado de cuenta para ver tus movimientos aquí."
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

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: colors.background,
  },
  header: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.lg,
    gap: 2,
  },
  searchRow: {
    marginHorizontal: spacing.xl,
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.lg,
    height: 46,
    borderWidth: 1,
    borderColor: colors.border,
  },
  searchInput: {
    flex: 1,
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
    padding: 0,
  },
  filtersRow: {
    marginTop: spacing.md,
    maxHeight: 48,
  },
  filters: {
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
    alignItems: 'center',
  },
  list: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.lg,
  },
  loading: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.xl,
    gap: spacing.lg,
  },
  footer: {
    marginVertical: spacing.lg,
  },
});
