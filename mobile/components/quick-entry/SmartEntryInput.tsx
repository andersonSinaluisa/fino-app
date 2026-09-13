import { StyleSheet, TextInput, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing, typography } from '../../theme';
import { Typo } from '../ui/Typo';
import { formatCurrency } from '../../utils/format';
import type { ParsedEntry } from '../../utils/quickEntry/parseNaturalEntry';
import type { Category } from '../../types/api';

/**
 * §7 ("input inteligente") y §11 ("confirmación inteligente").
 *
 * El §11 es explícito sobre lo que NO se debe hacer: después de interpretar "8 uber
 * ayer" no se abre un formulario completo. Se muestra UNA línea -- "✓ $8.00 ·
 * Transporte · Ayer" -- y un Guardar. Eso es todo lo que hay aquí.
 *
 * El campo no autoguarda (§11: "NO autoguardar texto natural durante la primera
 * versión"). La vista previa es para que la persona confirme de un vistazo que Fino
 * entendió bien, no para ahorrarle el toque de Guardar.
 */

interface SmartEntryInputProps {
  value: string;
  onChangeText: (text: string) => void;
  parsed: ParsedEntry | null;
  /** La categoría real ya resuelta contra las de la persona, o null. */
  category: Category | null;
  onSubmit?: () => void;
  disabled?: boolean;
  /** El botón de micrófono, si la voz está disponible en este dispositivo. */
  accessory?: React.ReactNode;
  /**
   * Estado de la voz en texto ("Escuchando...", "No te escuché"). Se pinta en el
   * flujo vertical del campo, debajo del input, para que empuje el layout en vez de
   * superponerse al enlace "Más detalles".
   */
  voiceStatus?: string | null;
  /** True cuando `voiceStatus` es un error y debe leerse como tal. */
  voiceStatusIsError?: boolean;
}

export function SmartEntryInput({
  value,
  onChangeText,
  parsed,
  category,
  onSubmit,
  disabled = false,
  accessory,
  voiceStatus,
  voiceStatusIsError = false,
}: SmartEntryInputProps) {
  const understood = parsed !== null && parsed.canSave;

  return (
    <View style={styles.container}>
      <Typo variant="body" color={colors.textSecondary}>
        ¿Qué pagaste?
      </Typo>

      <View style={styles.field}>
        <TextInput
          testID="smart-entry-input"
          value={value}
          onChangeText={onChangeText}
          // §41 ("copy"): un ejemplo real, no una instrucción. "Ej. 5 almuerzo"
          // enseña la sintaxis sin explicarla.
          placeholder="Ej. 5 almuerzo"
          placeholderTextColor={colors.textSecondary}
          style={styles.input}
          editable={!disabled}
          autoCorrect={false}
          autoCapitalize="none"
          returnKeyType="done"
          onSubmitEditing={onSubmit}
          accessibilityLabel="Describe el movimiento en lenguaje natural"
        />

        {accessory}
      </View>

      {voiceStatus ? (
        <View style={styles.preview} accessibilityLiveRegion="polite">
          <Ionicons
            name={voiceStatusIsError ? 'alert-circle-outline' : 'mic-outline'}
            size={16}
            color={voiceStatusIsError ? colors.danger : colors.textSecondary}
          />
          <Typo
            variant="caption"
            color={voiceStatusIsError ? colors.danger : colors.textSecondary}
            style={styles.previewText}
          >
            {voiceStatus}
          </Typo>
        </View>
      ) : null}

      {understood ? (
        <View style={styles.preview} accessibilityLiveRegion="polite">
          <Ionicons name="checkmark-circle" size={16} color={colors.success} />

          {/* La línea del §11, exactamente: monto, categoría y fecha separados por
              puntos medios. Los campos que Fino no reconoció simplemente no
              aparecen -- no se muestran huecos ni "desconocido". */}
          <Typo variant="body" numberOfLines={1} style={styles.previewText}>
            <Typo variant="bodyStrong" tabular>
              {formatCurrency(parsed.amount ?? 0, { signed: false })}
            </Typo>
            {parsed.description ? ` · ${parsed.description}` : ''}
            {category ? ` · ${category.name}` : ''}
            {parsed.dateLabel && parsed.dateLabel !== 'Hoy' ? ` · ${parsed.dateLabel}` : ''}
            {parsed.direction === 'Income' ? ' · Ingreso' : ''}
          </Typo>
        </View>
      ) : value.trim().length > 0 ? (
        <View style={styles.preview}>
          <Ionicons name="information-circle-outline" size={16} color={colors.textSecondary} />

          {/* §12: sin monto no se guarda. Se dice qué falta, no "error de
              interpretación" -- la persona no escribió nada mal, solo le falta el
              número. */}
          <Typo variant="caption" color={colors.textSecondary} style={styles.previewText}>
            Escribe también el monto, por ejemplo "5 almuerzo".
          </Typo>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: spacing.sm,
  },
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
  },
  input: {
    flex: 1,
    minHeight: 52,
    color: colors.text,
    fontSize: typography.body.fontSize,
    fontWeight: '500',
  },
  preview: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.xs,
  },
  previewText: {
    flex: 1,
  },
});
