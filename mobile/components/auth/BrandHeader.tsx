import { StyleSheet, View, type ImageSourcePropType } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';
import { BrandMark } from './BrandMark';

interface BrandHeaderProps {
  title?: string;
  tagline?: string;
  /** Isotipo oficial, cuando exista. Ver BrandMark. */
  markSource?: ImageSourcePropType;
}

/**
 * Bloque de marca compartido para las pantallas de auth: isotipo (si existe)
 * + wordmark + una sola frase corta. Separado de LoginScreen para que un
 * futuro estado "qué bueno verte de nuevo" (con biometría) pueda reusarlo
 * con otro tagline sin duplicar el layout.
 */
export function BrandHeader({ title = 'Fino', tagline = 'Tu dinero, más claro.', markSource }: BrandHeaderProps) {
  return (
    <View style={styles.wrapper}>
      <BrandMark size={40} source={markSource} />
      <Typo variant="title">{title}</Typo>
      <Typo variant="body" color={colors.textSecondary}>
        {tagline}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    marginTop: spacing.xl,
    marginBottom: spacing.xxl,
    gap: spacing.sm,
  },
});
