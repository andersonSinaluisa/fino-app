import { Fragment } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';

const STAGES = ['conecta', 'importa', 'listo'] as const;
export type OnboardingStage = (typeof STAGES)[number];

const STAGE_LABEL: Record<OnboardingStage, string> = {
  conecta: 'Conecta',
  importa: 'Importa',
  listo: 'Listo',
};

interface OnboardingTopBarProps {
  stage: OnboardingStage;
  onBack?: () => void;
  /** Solo se dibuja "Ahora no" cuando se pasa esto -- no todas las pantallas del flujo dejan saltar. */
  onSkip?: () => void;
}

/**
 * Onboarding funcional: la barra de 3 etapas "Conecta ━━━ Importa ━━━ Listo"
 * del spec -- deliberadamente NO un "Paso 3 de 8". Vive en su propio
 * componente porque la usan 6 pantallas distintas (bienvenida, cómo
 * funciona, elegir banco, tutorial, requisitos, importar-en-modo-onboarding)
 * y el "Ahora no" (nunca un "Saltar" prominente) vive junto porque en la
 * práctica siempre aparecen juntos en la misma fila superior.
 */
export function OnboardingTopBar({ stage, onBack, onSkip }: OnboardingTopBarProps) {
  const currentIndex = STAGES.indexOf(stage);

  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        {onBack ? (
          <Pressable onPress={onBack} hitSlop={12} accessibilityRole="button" accessibilityLabel="Atrás">
            <Ionicons name="chevron-back" size={20} color={colors.text} />
          </Pressable>
        ) : (
          <View style={styles.placeholder} />
        )}

        {onSkip ? (
          <Pressable onPress={onSkip} hitSlop={12} accessibilityRole="button">
            <Typo variant="caption" color={colors.textSecondary}>
              Ahora no
            </Typo>
          </Pressable>
        ) : (
          <View style={styles.placeholder} />
        )}
      </View>

      <View style={styles.progress}>
        {STAGES.map((item, index) => (
          <Fragment key={item}>
            <Typo variant="overline" color={index <= currentIndex ? colors.text : colors.textSecondary}>
              {STAGE_LABEL[item]}
            </Typo>
            {index < STAGES.length - 1 ? (
              <View style={[styles.line, index < currentIndex ? styles.lineActive : null]} />
            ) : null}
          </Fragment>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing.lg,
    marginBottom: spacing.xl,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  placeholder: {
    width: 20,
    height: 20,
  },
  progress: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  line: {
    flex: 1,
    height: 2,
    borderRadius: 1,
    backgroundColor: colors.border,
  },
  lineActive: {
    backgroundColor: colors.text,
  },
});
