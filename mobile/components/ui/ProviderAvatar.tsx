import { StyleSheet, View } from 'react-native';
import { colors, radius } from '../../theme';
import { Typo } from './Typo';
import { initialsOf } from '../../utils/format';

interface ProviderAvatarProps {
  name: string;
  color?: string;
  size?: number;
}

/**
 * Institution mark. Real logos are trademarked assets we do not ship, so the
 * avatar is the institution's own brand colour plus its initials — recognisable
 * without pretending to be the bank's logo.
 */
export function ProviderAvatar({ name, color = colors.primary, size = 44 }: ProviderAvatarProps) {
  return (
    <View
      style={[
        styles.avatar,
        {
          width: size,
          height: size,
          borderRadius: size / 3,
          backgroundColor: withAlpha(color, 0.16),
        },
      ]}
    >
      <Typo variant={size >= 44 ? 'bodyStrong' : 'caption'} color={darken(color)}>
        {initialsOf(name)}
      </Typo>
    </View>
  );
}

function withAlpha(hex: string, alpha: number): string {
  const normalized = hex.replace('#', '');
  if (normalized.length !== 6) {
    return colors.surfaceSecondary;
  }

  const r = parseInt(normalized.slice(0, 2), 16);
  const g = parseInt(normalized.slice(2, 4), 16);
  const b = parseInt(normalized.slice(4, 6), 16);

  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/** Keeps initials legible on pale brand colours such as Pichincha's yellow. */
function darken(hex: string): string {
  const normalized = hex.replace('#', '');
  if (normalized.length !== 6) {
    return colors.text;
  }

  const r = parseInt(normalized.slice(0, 2), 16);
  const g = parseInt(normalized.slice(2, 4), 16);
  const b = parseInt(normalized.slice(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;

  if (luminance < 0.55) {
    return hex;
  }

  const factor = 0.45;
  const scale = (value: number) => Math.round(value * factor).toString(16).padStart(2, '0');
  return `#${scale(r)}${scale(g)}${scale(b)}`;
}

const styles = StyleSheet.create({
  avatar: {
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.md,
  },
});
