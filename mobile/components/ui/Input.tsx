import { forwardRef, useState, type ReactNode } from 'react';
import { StyleSheet, TextInput, View, type TextInputProps } from 'react-native';
import { colors, radius, spacing, typography } from '../../theme';
import { Typo } from './Typo';

interface InputProps extends TextInputProps {
  label: string;
  error?: string | null;
  hint?: string;
  /** Icono/acción fijo a la derecha del campo (p. ej. mostrar/ocultar contraseña). */
  rightElement?: ReactNode;
}

export const Input = forwardRef<TextInput, InputProps>(function Input(
  { label, error, hint, rightElement, style, ...rest },
  ref,
) {
  const [focused, setFocused] = useState(false);

  return (
    <View style={styles.wrapper}>
      <Typo variant="overline" color={colors.textSecondary}>
        {label.toUpperCase()}
      </Typo>

      <View style={styles.fieldRow}>
        <TextInput
          {...rest}
          ref={ref}
          onFocus={(event) => {
            setFocused(true);
            rest.onFocus?.(event);
          }}
          onBlur={(event) => {
            setFocused(false);
            rest.onBlur?.(event);
          }}
          placeholderTextColor={colors.textSecondary}
          style={[
            styles.input,
            focused ? styles.focused : null,
            error ? styles.errored : null,
            style,
          ]}
        />

        {rightElement ? <View style={styles.rightElement}>{rightElement}</View> : null}
      </View>

      {error ? (
        <Typo variant="caption" color={colors.danger}>
          {error}
        </Typo>
      ) : hint ? (
        <Typo variant="caption" color={colors.textSecondary}>
          {hint}
        </Typo>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  wrapper: {
    gap: spacing.sm,
  },
  fieldRow: {
    justifyContent: 'center',
  },
  input: {
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.border,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.lg,
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
  },
  focused: {
    borderColor: colors.primary,
  },
  errored: {
    borderColor: colors.danger,
  },
  rightElement: {
    position: 'absolute',
    right: spacing.lg,
  },
});
