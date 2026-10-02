import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, ProviderAvatar, Typo } from '../ui';
import { UtilizationBar } from './UtilizationBar';
import { formatCurrency } from '../../utils/format';
import { dueLabel, formatDueDate } from '../../utils/creditCards';
import type { CreditCardSummary } from '../../types/api';

interface CreditCardTileProps {
  card: CreditCardSummary;
  hidden: boolean;
  onPress: (card: CreditCardSummary) => void;
}

/**
 * Una tarjeta en "Cuentas → Tarjetas de crédito". Dice lo que debe y lo que
 * toca pagar después; el cupo disponible aparece como crédito, nunca como
 * dinero (no hay un "saldo" verde aquí).
 */
export function CreditCardTile({ card, hidden, onPress }: CreditCardTileProps) {
  const money = (value: number) => formatCurrency(value, { hidden });
  const next = card.nextPayment;

  return (
    <Pressable
      onPress={() => onPress(card)}
      accessibilityRole="button"
      accessibilityLabel={`${card.name}, deuda ${money(card.currentDebt)}`}
      style={({ pressed }) => [styles.card, pressed ? styles.pressed : null]}
    >
      <View style={styles.header}>
        <ProviderAvatar name={card.providerName} color={card.brandColor} size={36} />
        <View style={styles.identity}>
          <Typo variant="subheading" numberOfLines={1}>
            {card.name}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {[card.lastFour ? `•••• ${card.lastFour}` : null, card.providerName].filter(Boolean).join('  ·  ')}
          </Typo>
        </View>
        <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
      </View>

      <View style={styles.row}>
        <View>
          <Typo variant="overline" color={colors.textSecondary}>
            DEUDA
          </Typo>
          <Typo variant="heading" tabular>
            {money(card.currentDebt)}
          </Typo>
        </View>
        {next ? (
          <View style={styles.alignEnd}>
            <Typo variant="overline" color={next.isOverdue ? colors.danger : colors.textSecondary}>
              PRÓXIMO PAGO · {formatDueDate(next.dueDate)}
            </Typo>
            <Typo variant="bodyStrong" tabular color={next.isOverdue ? colors.danger : colors.text}>
              {money(next.amount)}
            </Typo>
          </View>
        ) : null}
      </View>

      {card.needsSetup ? (
        <Badge label="Completa los datos de la tarjeta" tone="attention" />
      ) : (
        <View style={styles.utilization}>
          <UtilizationBar utilizationPercent={card.utilizationPercent} isOverLimit={card.isOverLimit} height={6} />
          <View style={styles.row}>
            <Typo variant="caption" color={colors.textSecondary}>
              Cupo disponible {card.availableCredit === null ? '—' : money(card.availableCredit)}
            </Typo>
            {next ? (
              <Typo variant="caption" color={next.isOverdue ? colors.danger : colors.textSecondary}>
                {dueLabel(next)}
              </Typo>
            ) : null}
          </View>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
  pressed: {
    opacity: 0.7,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  identity: {
    flex: 1,
    gap: 2,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  alignEnd: {
    alignItems: 'flex-end',
  },
  utilization: {
    gap: spacing.sm,
  },
});
