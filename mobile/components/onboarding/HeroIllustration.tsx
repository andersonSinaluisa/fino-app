import type { ComponentProps } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, elevation, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';

type IconName = ComponentProps<typeof Ionicons>['name'];

interface FloatingChipProps {
  icon: IconName;
  iconBackground: string;
  iconColor: string;
  title: string;
  subtitle: string;
  style: ViewStyle;
}

function FloatingChip({ icon, iconBackground, iconColor, title, subtitle, style }: FloatingChipProps) {
  return (
    <View style={[styles.chip, style]}>
      <View style={[styles.chipIcon, { backgroundColor: iconBackground }]}>
        <Ionicons name={icon} size={13} color={iconColor} />
      </View>
      <View>
        <Typo style={styles.chipTitle}>{title}</Typo>
        <Typo style={styles.chipSubtitle}>{subtitle}</Typo>
      </View>
    </View>
  );
}

/**
 * The "converging cards" hero from `design/onboarding_nexo/code.html`: a
 * central "everything's connected" summary card with real-looking (but
 * illustrative -- this screen renders before login, there is no session yet)
 * bank/wallet/import chips floating around it. Purely decorative -- no data
 * fetching, nothing tappable.
 */
export function HeroIllustration() {
  return (
    <View style={styles.container}>
      <View style={styles.hubCard}>
        <View style={styles.hubTopRow}>
          <View style={styles.hubLiveRow}>
            <View style={styles.hubDot} />
            <Typo style={styles.hubLiveLabel}>TODO CONECTADO</Typo>
          </View>
          <View style={styles.hubCountPill}>
            <Typo style={styles.hubCountText}>4 cuentas</Typo>
          </View>
        </View>

        <Typo style={styles.hubCaption}>Tu saldo disponible</Typo>
        <View style={styles.hubAmountRow}>
          <Typo style={styles.hubAmount}>$2,846</Typo>
          <Typo style={styles.hubAmountCents}>.20</Typo>
        </View>

        <View style={styles.hubPillsRow}>
          <View style={styles.hubMiniPill}>
            <Typo style={styles.hubMiniPillText}>Pichincha</Typo>
          </View>
          <View style={styles.hubMiniPill}>
            <Typo style={styles.hubMiniPillText}>Guayaquil</Typo>
          </View>
          <View style={styles.hubMiniPill}>
            <Typo style={styles.hubMiniPillText}>DEUNA</Typo>
          </View>
          <View style={[styles.hubMiniPill, styles.hubMiniPillAccent]}>
            <Typo style={styles.hubMiniPillText}>+1</Typo>
          </View>
        </View>
      </View>

      <FloatingChip
        icon="business-outline"
        iconBackground="#FFF5DC"
        iconColor="#996B00"
        title="Banco Pichincha"
        subtitle="$1,240.50"
        style={styles.chipTopLeft}
      />
      <FloatingChip
        icon="wallet-outline"
        iconBackground="#E5F9EE"
        iconColor="#1E824C"
        title="DEUNA"
        subtitle="$485.50"
        style={styles.chipTopRight}
      />
      <FloatingChip
        icon="card-outline"
        iconBackground="#FCE8E6"
        iconColor="#C0392B"
        title="Banco Guayaquil"
        subtitle="$860.20"
        style={styles.chipBottomLeft}
      />
      <FloatingChip
        icon="document-text-outline"
        iconBackground="rgba(199, 243, 107, 0.35)"
        iconColor={colors.text}
        title="CSV / Excel"
        subtitle="Importación fácil"
        style={styles.chipBottomRight}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    height: 250,
    alignItems: 'center',
    justifyContent: 'center',
  },
  hubCard: {
    width: 236,
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    padding: spacing.lg,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
    ...elevation.raised,
  },
  hubTopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.sm,
  },
  hubLiveRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  hubDot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: colors.accent,
  },
  hubLiveLabel: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.6,
    color: colors.textSecondary,
  },
  hubCountPill: {
    backgroundColor: colors.background,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  hubCountText: {
    fontSize: 10,
    fontWeight: '700',
    color: colors.text,
  },
  hubCaption: {
    fontSize: 11,
    fontWeight: '500',
    color: colors.textSecondary,
  },
  hubAmountRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    marginTop: 2,
    marginBottom: spacing.sm,
  },
  hubAmount: {
    fontSize: 28,
    fontWeight: '800',
    letterSpacing: -0.8,
    color: colors.text,
    lineHeight: 30,
  },
  hubAmountCents: {
    fontSize: 17,
    fontWeight: '600',
    color: colors.textSecondary,
    marginBottom: 2,
  },
  hubPillsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingTop: spacing.sm,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.background,
  },
  hubMiniPill: {
    backgroundColor: colors.background,
    borderRadius: 8,
    paddingHorizontal: 7,
    paddingVertical: 3,
  },
  hubMiniPillAccent: {
    backgroundColor: 'rgba(199, 243, 107, 0.4)',
  },
  hubMiniPillText: {
    fontSize: 10,
    fontWeight: '700',
    color: colors.text,
  },
  chip: {
    position: 'absolute',
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: 'rgba(255,255,255,0.94)',
    borderRadius: radius.lg,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: colors.border,
    ...elevation.card,
  },
  chipIcon: {
    width: 26,
    height: 26,
    borderRadius: 13,
    alignItems: 'center',
    justifyContent: 'center',
  },
  chipTitle: {
    fontSize: 11,
    fontWeight: '700',
    color: colors.text,
    lineHeight: 14,
  },
  chipSubtitle: {
    fontSize: 10,
    fontWeight: '600',
    color: colors.textSecondary,
    lineHeight: 13,
  },
  chipTopLeft: {
    top: 0,
    left: 4,
    transform: [{ rotate: '-6deg' }],
  },
  chipTopRight: {
    top: 10,
    right: 0,
    transform: [{ rotate: '6deg' }],
  },
  chipBottomLeft: {
    bottom: 6,
    left: 10,
    transform: [{ rotate: '3deg' }],
  },
  chipBottomRight: {
    bottom: 16,
    right: 8,
    transform: [{ rotate: '-3deg' }],
  },
});
