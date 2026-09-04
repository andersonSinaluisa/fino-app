import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCompactCurrency } from '../../utils/format';

interface MonthTilesProps {
  income: number;
  expense: number;
  hidden: boolean;
}

export function MonthTiles({ income, expense, hidden }: MonthTilesProps) {
  return (
    <View style={styles.row}>
      <Tile
        icon="arrow-down"
        label="Ingresaste"
        value={formatCompactCurrency(income, hidden)}
        sign="+"
        tone={colors.success}
        hidden={hidden}
      />
      <Tile
        icon="arrow-up"
        label="Gastaste"
        value={formatCompactCurrency(expense, hidden)}
        sign="-"
        tone={colors.text}
        hidden={hidden}
      />
    </View>
  );
}

interface TileProps {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  value: string;
  sign: string;
  tone: string;
  hidden: boolean;
}

function Tile({ icon, label, value, sign, tone, hidden }: TileProps) {
  return (
    <View style={styles.tile}>
      <View style={styles.head}>
        <Ionicons name={icon} size={13} color={colors.textSecondary} />
        <Typo variant="caption" color={colors.textSecondary}>
          {label}
        </Typo>
      </View>

      <Typo variant="heading" color={tone} tabular>
        {hidden ? '••••' : `${sign}${value}`}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  tile: {
    flex: 1,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  head: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs + 2,
  },
});
