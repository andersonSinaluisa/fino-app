import { StyleSheet, Text as RNText, type TextProps, type TextStyle } from 'react-native';
import { colors, typography, type TypographyVariant } from '../../theme';

interface TypoProps extends TextProps {
  variant?: TypographyVariant;
  color?: string;
  align?: TextStyle['textAlign'];
  /** Tabular figures keep money columns aligned while values change. */
  tabular?: boolean;
}

/**
 * The only text primitive in the app. Every size, weight and letter-spacing comes
 * from the type scale, so hierarchy stays consistent without anyone repeating
 * font sizes across screens.
 */
export function Typo({
  variant = 'body',
  color = colors.text,
  align,
  tabular = false,
  style,
  ...rest
}: TypoProps) {
  return (
    <RNText
      {...rest}
      style={[
        styles.base,
        typography[variant] as TextStyle,
        { color },
        align ? { textAlign: align } : null,
        tabular ? styles.tabular : null,
        style,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  base: {
    includeFontPadding: false,
  },
  tabular: {
    fontVariant: ['tabular-nums'],
  },
});
