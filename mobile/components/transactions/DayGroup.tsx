import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { TransactionRow } from './TransactionRow';
import { formatCurrency, type DayGroup as DayGroupModel } from '../../utils/format';
import type { TransactionListItem } from '../../types/api';

interface DayGroupProps {
  group: DayGroupModel<TransactionListItem>;
  hidden?: boolean;
  onSelect?: (transaction: TransactionListItem) => void;
}

/** A day of movements with its net total, the way a statement reads. */
export function DayGroupSection({ group, hidden, onSelect }: DayGroupProps) {
  return (
    <View style={styles.section}>
      <View style={styles.header}>
        <Typo variant="overline" color={colors.textSecondary}>
          {group.heading}
        </Typo>
        <Typo variant="overline" color={colors.textSecondary} tabular>
          {hidden ? '••••' : formatCurrency(group.total, { signed: true })}
        </Typo>
      </View>

      <View style={styles.card}>
        {group.items.map((transaction, index) => (
          <View key={transaction.id}>
            {index > 0 ? <View style={styles.divider} /> : null}
            <TransactionRow transaction={transaction} hidden={hidden} onPress={onSelect} />
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    marginBottom: spacing.xl,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: spacing.sm,
    paddingHorizontal: spacing.xs,
  },
  card: {
    backgroundColor: colors.surface,
    borderRadius: 22,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.xs,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
});
