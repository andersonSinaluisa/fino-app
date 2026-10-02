import { Pressable, StyleSheet, Switch, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Typo } from '../ui';

interface ReserveToggleProps {
  value: boolean;
  onChange: (value: boolean) => void;
}

/**
 * "Reservar próximo pago": misma tarjeta grande y explícita que "Reservar este
 * dinero" en Presupuestos (un switch suelto no se veía). Encendido, el próximo
 * pago de la tarjeta entra en Comprometido; apagado, la deuda se sigue viendo
 * pero no baja tu Disponible.
 */
export function ReserveToggle({ value, onChange }: ReserveToggleProps) {
  return (
    <Pressable
      onPress={() => onChange(!value)}
      accessibilityRole="switch"
      accessibilityState={{ checked: value }}
      accessibilityLabel="Reservar próximo pago"
      accessibilityHint={value ? 'Toca para que la deuda no reduzca tu Disponible' : 'Toca para apartar el próximo pago de tu Disponible'}
      style={({ pressed }) => [styles.card, value ? styles.cardOn : null, pressed ? styles.pressed : null]}
    >
      <View style={[styles.icon, value ? styles.iconOn : null]}>
        <Ionicons name={value ? 'lock-closed' : 'lock-open-outline'} size={20} color={value ? colors.onAccent : colors.text} />
      </View>
      <View style={styles.flex}>
        <View style={styles.titleRow}>
          <Typo variant="bodyStrong">Reservar próximo pago</Typo>
          <View style={[styles.state, value ? styles.stateOn : null]}>
            <Typo variant="overline" color={value ? colors.onPrimary : colors.textSecondary}>
              {value ? 'SÍ' : 'NO'}
            </Typo>
          </View>
        </View>
        <Typo variant="caption" color={value ? colors.text : colors.textSecondary}>
          Fino puede reservar este dinero al calcular cuánto tienes realmente disponible.
          {value ? ' Solo el próximo pago, no toda la deuda.' : ' Apagado, la deuda se sigue viendo pero no baja tu Disponible.'}
        </Typo>
      </View>
      <Switch
        value={value}
        onValueChange={onChange}
        trackColor={{ true: colors.primary, false: colors.borderStrong }}
        thumbColor={value ? colors.accent : colors.surface}
        ios_backgroundColor={colors.borderStrong}
        accessibilityElementsHidden
        importantForAccessibility="no"
      />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 2,
    borderColor: colors.borderStrong,
    padding: spacing.lg,
  },
  cardOn: {
    borderColor: colors.primary,
    backgroundColor: 'rgba(199, 243, 107, 0.28)',
  },
  pressed: {
    opacity: 0.85,
  },
  icon: {
    width: 40,
    height: 40,
    borderRadius: 20,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconOn: {
    backgroundColor: colors.accent,
  },
  flex: {
    flex: 1,
  },
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginBottom: 2,
  },
  state: {
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    borderRadius: radius.pill,
    backgroundColor: colors.surfaceSecondary,
  },
  stateOn: {
    backgroundColor: colors.primary,
  },
});
