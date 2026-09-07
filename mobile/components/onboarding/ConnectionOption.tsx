import type { ComponentProps, ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, elevation, radius, spacing } from '../../theme';
import { Typo } from '../ui/Typo';

type IconName = ComponentProps<typeof Ionicons>['name'];

interface ConnectionOptionProps {
  icon: IconName;
  iconBackground: string;
  iconColor: string;
  title: string;
  tagLabel: string;
  tagTone: 'positive' | 'accent' | 'neutral';
  description: string;
  selected: boolean;
  onSelect: () => void;
  /** Optional row of small chips or a footnote rendered under the description. */
  footer?: ReactNode;
}

const tagPalette: Record<ConnectionOptionProps['tagTone'], { background: string; text: string }> = {
  positive: { background: 'rgba(78, 159, 115, 0.14)', text: colors.success },
  accent: { background: 'rgba(199, 243, 107, 0.5)', text: colors.text },
  neutral: { background: colors.surfaceSecondary, text: colors.textSecondary },
};

/**
 * One of the three radio-style "what do you want to connect" cards from
 * `design/paso_2_onboarding_nexo/code.html` (banks / wallets / file import).
 * A plain selectable card, not a real HTML radio group -- state lives in the
 * parent screen.
 */
export function ConnectionOption({
  icon,
  iconBackground,
  iconColor,
  title,
  tagLabel,
  tagTone,
  description,
  selected,
  onSelect,
  footer,
}: ConnectionOptionProps) {
  const tag = tagPalette[tagTone];

  return (
    <Pressable
      onPress={onSelect}
      accessibilityRole="radio"
      accessibilityState={{ checked: selected }}
      style={[styles.card, selected ? styles.cardSelected : null]}
    >
      <View style={styles.topRow}>
        <View style={styles.leftRow}>
          <View style={[styles.iconBadge, { backgroundColor: iconBackground }]}>
            <Ionicons name={icon} size={19} color={iconColor} />
          </View>
          <View style={styles.textBlock}>
            <View style={styles.titleRow}>
              <Typo style={styles.title}>{title}</Typo>
              <View style={[styles.tag, { backgroundColor: tag.background }]}>
                <Typo style={[styles.tagText, { color: tag.text }]}>{tagLabel}</Typo>
              </View>
            </View>
            <Typo style={styles.description}>{description}</Typo>
          </View>
        </View>

        <View style={[styles.radio, selected ? styles.radioSelected : null]}>
          {selected ? <View style={styles.radioDot} /> : null}
        </View>
      </View>

      {footer ? <View style={styles.footer}>{footer}</View> : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.xl,
    borderWidth: 2,
    borderColor: 'transparent',
    padding: spacing.lg,
    ...elevation.card,
  },
  cardSelected: {
    borderColor: colors.primary,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  leftRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    flex: 1,
  },
  iconBadge: {
    width: 44,
    height: 44,
    borderRadius: 18,
    alignItems: 'center',
    justifyContent: 'center',
  },
  textBlock: {
    flex: 1,
    gap: 3,
  },
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  title: {
    fontSize: 16,
    fontWeight: '700',
    color: colors.text,
  },
  tag: {
    borderRadius: radius.pill,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  tagText: {
    fontSize: 10,
    fontWeight: '700',
  },
  description: {
    fontSize: 12,
    color: colors.textSecondary,
    lineHeight: 16,
  },
  radio: {
    width: 20,
    height: 20,
    borderRadius: 10,
    borderWidth: 2,
    borderColor: colors.borderStrong,
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: 2,
  },
  radioSelected: {
    borderColor: colors.primary,
    backgroundColor: colors.primary,
  },
  radioDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.surface,
  },
  footer: {
    marginTop: spacing.md,
    paddingTop: spacing.sm,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.background,
  },
});
