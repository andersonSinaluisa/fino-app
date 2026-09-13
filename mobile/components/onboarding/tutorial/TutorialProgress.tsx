import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../../theme';
import { Typo } from '../../ui';

interface TutorialProgressProps {
  step: number;
  total: number;
  onPrevious?: () => void;
  onNext?: () => void;
  /** El spec pide que nunca se obligue a esperar la animación -- si no hay onNext, no se dibuja "Siguiente". */
  showControls?: boolean;
}

/**
 * "● ○ ○ ○ / Paso 1 de 4 / [Anterior] [Siguiente]" del spec del tutorial de
 * banco -- deliberadamente NO el "1/8" del onboarding completo (ese usa la
 * barra de 3 etapas Conecta/Importa/Listo en su lugar). Este progreso es
 * interno al tutorial de un banco, independiente del progreso general.
 */
export function TutorialProgress({ step, total, onPrevious, onNext, showControls = true }: TutorialProgressProps) {
  return (
    <View style={styles.wrap}>
      <View style={styles.dots}>
        {Array.from({ length: total }, (_, index) => (
          <View key={index} style={[styles.dot, index === step - 1 ? styles.dotActive : null]} />
        ))}
      </View>

      <Typo variant="caption" color={colors.textSecondary}>
        Paso {step} de {total}
      </Typo>

      {showControls ? (
        <View style={styles.controls}>
          <Pressable
            onPress={onPrevious}
            disabled={!onPrevious}
            hitSlop={8}
            style={[styles.navButton, !onPrevious ? styles.navButtonDisabled : null]}
            accessibilityRole="button"
            accessibilityLabel="Paso anterior"
          >
            <Ionicons name="chevron-back" size={18} color={onPrevious ? colors.text : colors.textSecondary} />
            <Typo variant="caption" color={onPrevious ? colors.text : colors.textSecondary}>
              Anterior
            </Typo>
          </Pressable>

          <Pressable
            onPress={onNext}
            disabled={!onNext}
            hitSlop={8}
            style={[styles.navButton, !onNext ? styles.navButtonDisabled : null]}
            accessibilityRole="button"
            accessibilityLabel="Siguiente paso"
          >
            <Typo variant="caption" color={onNext ? colors.text : colors.textSecondary}>
              Siguiente
            </Typo>
            <Ionicons name="chevron-forward" size={18} color={onNext ? colors.text : colors.textSecondary} />
          </Pressable>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    alignItems: 'center',
    gap: spacing.sm,
  },
  dots: {
    flexDirection: 'row',
    gap: 6,
  },
  dot: {
    width: 6,
    height: 6,
    borderRadius: radius.pill,
    backgroundColor: colors.border,
  },
  dotActive: {
    backgroundColor: colors.text,
    width: 16,
  },
  controls: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    alignSelf: 'stretch',
    marginTop: spacing.xs,
  },
  navButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 2,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.sm,
  },
  navButtonDisabled: {
    opacity: 0.35,
  },
});
