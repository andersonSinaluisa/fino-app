import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import { iconForCategory } from '../../utils/categoryIcons';
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
  // Entregable 13: a confirmed transfer still shows in the list (it really did
  // move money) but reads as neither income nor an expense.
  const isTransfer = transaction.isInternalTransfer;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${transaction.description}, ${formatCurrency(transaction.signedAmount)}`}
      onPress={() => onPress?.(transaction)}
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
    >
      <View style={[styles.icon, { backgroundColor: iconBackground(transaction) }]}>
        <Ionicons name={iconName(transaction)} size={17} color={iconColor(transaction)} />
      </View>

      <View style={styles.body}>
        <Typo variant="bodyStrong" numberOfLines={1}>
          {transaction.merchant ?? transaction.description}
        </Typo>

        <View style={styles.metaRow}>
          <Typo variant="caption" color={colors.textSecondary} numberOfLines={1}>
            {isTransfer ? 'Transferencia interna' : (transaction.categoryName ?? 'Sin categoría')}
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
        // §22: un retiro conciliado no es un gasto ni un ingreso, es dinero
        // cambiando de sitio. La pata de efectivo se pintaba en verde de ingreso,
        // que hacía parecer que había entrado dinero nuevo al patrimonio.
        color={isTransfer ? colors.textSecondary : income ? colors.success : colors.text}
      >
        {hidden ? '••••' : formatCurrency(transaction.signedAmount, { signed: true })}
      </Typo>
    </Pressable>
  );
}

function Dot() {
  return <View style={styles.dot} />;
}

// Entregable: every movement used to show the same up/down arrow no matter its
// category, so scanning a list of expenses told you nothing at a glance. The
// category's own icon (already sent by the API as categoryIcon/categoryColor,
// never rendered until now) replaces that generic arrow; the arrow survives
// only as the fallback for a movement still waiting on a category.
function iconName(transaction: TransactionListItem): keyof typeof Ionicons.glyphMap {
  if (transaction.isInternalTransfer) {
    return 'swap-horizontal-outline';
  }

  if (transaction.categoryIcon) {
    return iconForCategory(transaction.categoryIcon);
  }

  return transaction.direction === 'Income' ? 'arrow-down' : 'arrow-up';
}

function iconColor(transaction: TransactionListItem): string {
  if (transaction.isInternalTransfer) {
    return colors.textSecondary;
  }

  if (transaction.categoryIcon && transaction.categoryColor) {
    return transaction.categoryColor;
  }

  return transaction.direction === 'Income' ? colors.success : colors.text;
}

function iconBackground(transaction: TransactionListItem): string {
  if (transaction.isInternalTransfer) {
    return colors.surfaceSecondary;
  }

  if (transaction.categoryColor) {
    return `${transaction.categoryColor}33`;
  }

  if (transaction.direction === 'Income') {
    return 'rgba(78, 159, 115, 0.14)';
  }

  return colors.surfaceSecondary;
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
