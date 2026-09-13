import { forwardRef, useState } from 'react';
import { Pressable, TextInput, type TextInputProps } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Input } from '../ui';

interface PasswordInputProps extends Omit<TextInputProps, 'secureTextEntry'> {
  label: string;
  error?: string | null;
}

/**
 * Input de contraseña de Fino: mismo Input compartido, con el ojo para
 * mostrar/ocultar (punto 3 del rediseño de Login) y algo más de aire que el
 * resto de la app (punto 3: 58-64px de alto, radio 18-22px).
 */
export const PasswordInput = forwardRef<TextInput, PasswordInputProps>(function PasswordInput(
  { label, error, style, ...rest },
  ref,
) {
  const [visible, setVisible] = useState(false);

  return (
    <Input
      {...rest}
      ref={ref}
      label={label}
      error={error}
      secureTextEntry={!visible}
      autoCapitalize="none"
      autoCorrect={false}
      style={[styles.input, style]}
      rightElement={
        <Pressable
          onPress={() => setVisible((current) => !current)}
          hitSlop={12}
          accessibilityRole="button"
          accessibilityLabel={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
        >
          <Ionicons name={visible ? 'eye-off-outline' : 'eye-outline'} size={20} color={colors.textSecondary} />
        </Pressable>
      }
    />
  );
});

const styles = {
  input: {
    minHeight: 60,
    borderRadius: radius.lg,
    paddingRight: spacing.xxl + spacing.md,
  },
} as const;
