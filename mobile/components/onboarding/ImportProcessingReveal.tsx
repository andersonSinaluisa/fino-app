import { useEffect, useRef } from 'react';
import { Animated, StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';
import { useReduceMotion } from '../../hooks/useReduceMotion';
import { formatCurrency } from '../../utils/format';
import type { ImportPreview } from '../../types/api';

interface ImportProcessingRevealProps {
  preview: ImportPreview;
  bankTitle: string;
}

/**
 * Onboarding funcional, Pantalla 7→8: en vez de saltar de "Procesando..." al
 * detalle técnico de siempre (filas, duplicados, mapeo), esto revela los
 * datos REALES del preview -- banco identificado, cuántos movimientos,
 * ingresos/gastos -- en tres tiempos cortos. Nunca es una espera falsa: solo
 * se monta cuando `preview` ya llegó del backend, así que lo único que se
 * anima es la REVELACIÓN de un resultado que ya existe, nunca el resultado
 * en sí. Con Reduce Motion, las tres líneas aparecen de una vez, sin stagger.
 */
export function ImportProcessingReveal({ preview, bankTitle }: ImportProcessingRevealProps) {
  const reduceMotion = useReduceMotion();
  const lines = useRef([0, 1, 2].map(() => new Animated.Value(0))).current;

  useEffect(() => {
    if (reduceMotion) {
      lines.forEach((line) => line.setValue(1));
      return;
    }

    lines.forEach((line) => line.setValue(0));
    Animated.stagger(
      180,
      lines.map((line) => Animated.timing(line, { toValue: 1, duration: 260, useNativeDriver: true })),
    ).start();
    // preview.importId identifies "this result" -- a new upload should replay
    // the reveal even if the row count happens to match the previous one.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preview.importId, reduceMotion]);

  const style = (line: Animated.Value) =>
    reduceMotion ? undefined : { opacity: line, transform: [{ translateY: line.interpolate({ inputRange: [0, 1], outputRange: [8, 0] }) }] };

  return (
    <View style={styles.wrap}>
      <Animated.View style={style(lines[0])}>
        <Typo variant="caption" color={colors.textSecondary}>
          {bankTitle} · {preview.fileName}
        </Typo>
      </Animated.View>

      <Animated.View style={style(lines[1])}>
        <Typo variant="title">
          {preview.status === 'Failed' ? 'No pudimos leerlo' : `Encontramos ${preview.totalRows} movimientos`}
        </Typo>
      </Animated.View>

      {preview.status !== 'Failed' ? (
        <Animated.View style={[styles.totals, style(lines[2])]}>
          <View style={styles.totalTile}>
            <Typo variant="caption" color={colors.textSecondary}>
              Ingresos
            </Typo>
            <Typo variant="subheading" color={colors.success} tabular>
              {formatCurrency(preview.incomeTotal)}
            </Typo>
          </View>
          <View style={styles.totalTile}>
            <Typo variant="caption" color={colors.textSecondary}>
              Gastos
            </Typo>
            <Typo variant="subheading" tabular>
              {formatCurrency(preview.expenseTotal)}
            </Typo>
          </View>
        </Animated.View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    gap: spacing.sm,
  },
  totals: {
    flexDirection: 'row',
    gap: spacing.md,
    marginTop: spacing.xs,
  },
  totalTile: {
    flex: 1,
    backgroundColor: colors.surface,
    borderRadius: 16,
    padding: spacing.md,
    gap: 2,
  },
});
