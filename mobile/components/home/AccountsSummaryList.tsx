import { Pressable, StyleSheet, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { SectionHeader } from '../ui/SectionHeader';
import { ProviderAvatar } from '../ui/ProviderAvatar';
import { Typo } from '../ui/Typo';
import {
  accountBalanceStatus,
  balanceStatusLabel,
  formatCurrency,
  type AccountBalanceStatus,
} from '../../utils/format';
import type { Account } from '../../types/api';

interface AccountsSummaryListProps {
  accounts: Account[];
  hidden: boolean;
  onSeeAll: () => void;
  onPressAccount: (account: Account) => void;
}

const MAX_VISIBLE = 3;

/**
 * Rediseño de Home (2026-09), regla de UX #3: máximo 3 cuentas en Home, en
 * filas compactas dentro de UNA sola tarjeta -- no una AccountCard grande por
 * cuenta como antes. "Ver todas" lleva a Cuentas, que sigue siendo la única
 * pantalla con el listado completo (misma AccountCard, sin tocarla).
 */
export function AccountsSummaryList({ accounts, hidden, onSeeAll, onPressAccount }: AccountsSummaryListProps) {
  if (accounts.length === 0) {
    return null;
  }

  const visible = accounts.slice(0, MAX_VISIBLE);

  return (
    <View>
      <SectionHeader title="Tus cuentas" actionLabel="Ver todas" onAction={onSeeAll} />
      <View style={styles.card}>
        {visible.map((account, index) => (
          <View key={account.id}>
            {index > 0 ? <View style={styles.divider} /> : null}
            <AccountSummaryRow account={account} hidden={hidden} onPress={() => onPressAccount(account)} />
          </View>
        ))}
      </View>
    </View>
  );
}

function AccountSummaryRow({
  account,
  hidden,
  onPress,
}: {
  account: Account;
  hidden: boolean;
  onPress: () => void;
}) {
  const status = accountBalanceStatus(account);

  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed ? styles.pressed : null]}
    >
      <ProviderAvatar name={account.providerName} color={account.brandColor} size={36} />

      <View style={styles.body}>
        <Typo variant="bodyStrong" numberOfLines={1}>
          {account.alias}
        </Typo>
        <Typo variant="caption" color={statusColor(status)}>
          {balanceStatusLabel(status)}
        </Typo>
      </View>

      <Typo variant="bodyStrong" tabular>
        {hidden ? '••••' : formatCurrency(account.balance)}
      </Typo>
    </Pressable>
  );
}

function statusColor(status: AccountBalanceStatus): string {
  switch (status) {
    case 'Verified':
      return colors.success;
    case 'NeedsUpdate':
      return colors.warning;
    default:
      return colors.textSecondary;
  }
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    paddingHorizontal: spacing.lg,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.md,
  },
  pressed: {
    opacity: 0.6,
  },
  body: {
    flex: 1,
    gap: 3,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
  },
});
