import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Card } from '../ui/Card';
import { Typo } from '../ui/Typo';
import { Badge } from '../ui/Badge';
import { ProviderAvatar } from '../ui/ProviderAvatar';
import { balanceTypeLabel, formatCurrency, formatRelativeTime, maskLabel } from '../../utils/format';
import type { Account } from '../../types/api';

interface AccountCardProps {
  account: Account;
  hidden?: boolean;
  onPress?: (account: Account) => void;
  compact?: boolean;
}

export function AccountCard({ account, hidden = false, onPress, compact = false }: AccountCardProps) {
  return (
    <Card onPress={onPress ? () => onPress(account) : undefined}>
      <View style={styles.header}>
        <ProviderAvatar name={account.providerName} color={account.brandColor} size={compact ? 36 : 44} />

        <View style={styles.identity}>
          <Typo variant="subheading" numberOfLines={1}>
            {account.alias}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {[maskLabel(account.mask), connectionLabel(account)].filter(Boolean).join('  ·  ')}
          </Typo>
        </View>
      </View>

      <View style={styles.amountRow}>
        <Typo variant="title" tabular>
          {hidden ? '••••••' : formatCurrency(account.balance)}
        </Typo>
      </View>

      <View style={styles.footer}>
        <Badge
          label={balanceTypeLabel(account.balanceType)}
          tone={account.balanceType === 'Verified' ? 'positive' : 'neutral'}
        />
        <Typo variant="caption" color={colors.textSecondary}>
          Actualizado {formatRelativeTime(account.lastSyncedAt ?? account.lastTransactionAt)}
        </Typo>
      </View>
    </Card>
  );
}

function connectionLabel(account: Account): string {
  switch (account.connectionMode) {
    case 'ManualImport':
      return 'Importación';
    case 'Email':
      return 'Correo';
    case 'Api':
      return 'Automática';
    case 'Webhook':
      return 'Tiempo real';
    default:
      return '';
  }
}

const styles = StyleSheet.create({
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  identity: {
    flex: 1,
    gap: 2,
  },
  amountRow: {
    marginTop: spacing.lg,
  },
  footer: {
    marginTop: spacing.md,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
});
