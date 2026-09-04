import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import type { TransactionListItem } from '../../types/api';

interface TransactionRowProps {
  transaction: TransactionListItem;
  hidden?: boolean;
  onPress?: (transaction: TransactionListItem) => void;
}

export function TransactionRow({ transaction, hidden = false, onPress }: TransactionRowProps) {
  const income = transaction.direction === 'Income';
  const needsReview = transaction.status === 'NeedsReview';
  const pending = transaction.status === 'Pending';

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${transaction.description}, ${formatCurrency(transaction.signedAmount)}`}
      onPress={() => onPress?.(transaction)}
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
    >
      <View style={[styles.icon, { backgroundColor: iconBackground(transaction) }]}>
        <Ionicons
          name={income ? 'arrow-down' : 'arrow-up'}
          size={16}
          color={income ? colors.success : colors.text}
        />
      </View>

      <View style={styles.body}>
        <Typo variant="bodyStrong" numberOfLines={1}>
          {transaction.merchant ?? transaction.description}
        </Typo>

        <View style={styles.metaRow}>
          <Typo variant="caption" color={colors.textSecondary} numberOfLines={1}>
            {transaction.categoryName ?? 'Sin categoría'}
          </Typo>

          {needsReview ? (
            <>
              <Dot />
              <Typo variant="caption" color={colors.warning}>
                Posible duplicado
              </Typo>
            </>
          ) : pending ? (
            <>
              <Dot />
              <Typo variant="caption" color={colors.textSecondary}>
                Por confirmar
              </Typo>
            </>
          ) : null}
        </View>
      </View>

      <Typo
        variant="bodyStrong"
        tabular
        color={income ? colors.success : colors.text}
      >
        {hidden ? '••••' : formatCurrency(transaction.signedAmount, { signed: true })}
      </Typo>
    </Pressable>
  );
}

function Dot() {
  return <View style={styles.dot} />;
}

function iconBackground(transaction: TransactionListItem): string {
  if (transaction.direction === 'Income') {
    return 'rgba(78, 159, 115, 0.14)';
  }

  return transaction.categoryColor ? `${transaction.categoryColor}33` : colors.surfaceSecondary;
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
  },
  pressed: {
    opacity: 0.6,
  },
  icon: {
    width: 40,
    height: 40,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  body: {
    flex: 1,
    gap: 3,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  dot: {
    width: 3,
    height: 3,
    borderRadius: 2,
    backgroundColor: colors.textSecondary,
  },
});
