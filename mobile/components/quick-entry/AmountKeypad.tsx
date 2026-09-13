import { memo } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, radius, spacing, typography } from '../../theme';
import { Typo } from '../ui/Typo';

/**
 * §4 ("teclado numérico"). Fino trae su propio teclado en vez de usar el nativo por
 * dos razones concretas, no por gusto:
 *
 *  1. Velocidad constante. El teclado del sistema tarda 200-300 ms en aparecer y
 *     empuja el layout al hacerlo. Con un objetivo de 1-3 segundos de principio a
 *     fin, esa animación es una parte visible del presupuesto.
 *  2. Reglas de dinero. Aquí se puede impedir el segundo separador decimal, el
 *     tercer decimal y el signo negativo (§4) en el sitio donde se pulsa la tecla,
 *     en lugar de sanear después un texto que el teclado ya dejó escribir.
 *
 * El componente no guarda estado: recibe el texto y devuelve el siguiente. Así la
 * misma lógica de tecleo se puede probar sin renderizar nada.
 */

/** §4: "más de dos decimales para moneda" no se permite. */
const MAX_DECIMALS = 2;

/** Un tope de dígitos enteros que nadie alcanza escribiendo un gasto real. */
const MAX_INTEGER_DIGITS = 7;

export type KeypadKey = '0' | '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9' | '.' | 'delete';

/**
 * Aplica una pulsación al texto actual. Función pura y exportada a propósito: es
 * donde viven las reglas del §4, y donde las prueban los tests sin montar la UI.
 */
export function applyKey(current: string, key: KeypadKey): string {
  if (key === 'delete') {
    return current.slice(0, -1);
  }

  if (key === '.') {
    // Un solo separador decimal. Pulsarlo otra vez no hace nada en vez de producir
    // "5..0", que luego habría que limpiar en otro sitio.
    if (current.includes('.')) {
      return current;
    }

    // "." a secas se escribe como "0." para que el campo nunca muestre algo que no
    // es un número.
    return current.length === 0 ? '0.' : `${current}.`;
  }

  const [integerPart, decimalPart] = current.split('.');

  if (decimalPart !== undefined) {
    if (decimalPart.length >= MAX_DECIMALS) {
      return current;
    }

    return `${integerPart}.${decimalPart}${key}`;
  }

  // Sin ceros a la izquierda: "0" seguido de "5" es 5, no "05".
  if (current === '0') {
    return key;
  }

  if (integerPart.length >= MAX_INTEGER_DIGITS) {
    return current;
  }

  return current + key;
}

interface AmountKeypadProps {
  onKey: (key: KeypadKey) => void;
  /** Se desactiva mientras se guarda, para que no se pueda seguir tecleando. */
  disabled?: boolean;
}

const ROWS: readonly (readonly KeypadKey[])[] = [
  ['1', '2', '3'],
  ['4', '5', '6'],
  ['7', '8', '9'],
  ['.', '0', 'delete'],
];

const LABELS: Partial<Record<KeypadKey, string>> = {
  '.': 'Punto decimal',
  delete: 'Borrar',
};

function KeypadComponent({ onKey, disabled = false }: AmountKeypadProps) {
  return (
    <View style={styles.grid} accessibilityRole="none">
      {ROWS.map((row, rowIndex) => (
        <View key={rowIndex} style={styles.row}>
          {row.map((key) => (
            <Pressable
              key={key}
              testID={`keypad-${key}`}
              accessibilityRole="button"
              accessibilityLabel={LABELS[key] ?? key}
              disabled={disabled}
              onPress={() => {
                // §33: "tap keypad: muy ligero." Y siempre tolerante a fallo: si el
                // dispositivo no soporta háptica, teclear no puede romperse.
                void Haptics.selectionAsync().catch(() => undefined);
                onKey(key);
              }}
              style={({ pressed }) => [
                styles.key,
                pressed && !disabled ? styles.keyPressed : null,
                disabled ? styles.keyDisabled : null,
              ]}
            >
              {key === 'delete' ? (
                <Ionicons name="backspace-outline" size={24} color={colors.text} />
              ) : (
                <Typo style={styles.keyLabel} tabular>
                  {key}
                </Typo>
              )}
            </Pressable>
          ))}
        </View>
      ))}
    </View>
  );
}

export const AmountKeypad = memo(KeypadComponent);

const styles = StyleSheet.create({
  grid: {
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  key: {
    flex: 1,
    // §37: muy por encima del mínimo de 44px. Es la tecla que más se pulsa de toda
    // la app y se pulsa con prisa.
    height: 56,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.md,
    backgroundColor: colors.surface,
  },
  keyPressed: {
    backgroundColor: colors.surfaceSecondary,
  },
  keyDisabled: {
    opacity: 0.4,
  },
  keyLabel: {
    fontSize: typography.heading.fontSize,
    lineHeight: typography.heading.lineHeight,
    fontWeight: '600',
    color: colors.text,
  },
});
