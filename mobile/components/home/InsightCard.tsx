import { StyleSheet, View, type ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';
import type { Insight } from '../../types/api';

interface InsightCardProps {
  insight: Insight;
  style?: ViewStyle;
}

const iconFor: Record<string, keyof typeof Ionicons.glyphMap> = {
  MONTHLY_SPEND: 'wallet-outline',
  MONTH_OVER_MONTH: 'trending-down-outline',
  TOP_CATEGORY: 'pie-chart-outline',
  CATEGORY_CHANGE: 'swap-vertical-outline',
  RECURRING_SUBSCRIPTION: 'repeat-outline',
  FREQUENT_MERCHANT: 'storefront-outline',
  HIGHEST_SPEND_DAY: 'calendar-outline',
  INCOME_VS_EXPENSE: 'scale-outline',
  STALE_ACCOUNT: 'refresh-outline',
};

export function InsightCard({ insight, style }: InsightCardProps) {
  const accent =
    insight.severity === 'Positive'
      ? colors.accentSecondary
      : insight.severity === 'Attention'
        ? colors.warning
        : colors.accent;

  return (
    <View style={[styles.card, style]}>
      <View style={[styles.icon, { backgroundColor: `${accent}33` }]}>
        <Ionicons name={iconFor[insight.code] ?? 'sparkles-outline'} size={16} color={colors.text} />
      </View>

      <Typo variant="subheading" numberOfLines={2}>
        {insight.title}
      </Typo>

      <Typo variant="caption" color={colors.textSecondary} numberOfLines={3}>
        {insight.body}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    width: 232,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  icon: {
    width: 34,
    height: 34,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
});
