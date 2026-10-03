import { useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, radius, spacing } from '../../theme';
import { Button, Typo } from '../ui';
import { useRulePreview } from '../../hooks/queries';
import {
  normalizeForDisplay,
  patternFromRange,
  rangeForPattern,
  splitWords,
  toggleWord,
  type WordRange,
} from '../../utils/rulePattern';
import type { Category, RulePreview } from '../../types/api';

export interface RuleScopeChoice {
  createRule: boolean;
  applyToExistingMatches: boolean;
  /** null = la sugerencia de Fino (o sin regla). */
  rulePattern: string | null;
}

interface RuleScopeSheetProps {
  transactionId: string;
  description: string;
  category: Category;
  /** La vista previa con la sugerencia de Fino, ya pedida antes de abrir. */
  preview: RulePreview;
  busy: boolean;
  onChoose: (choice: RuleScopeChoice) => void;
  onClose: () => void;
}

const PREVIEW_DELAY_MS = 350;

/**
 * "¿Aplicar a otros movimientos?" -- reemplaza el Alert que solo mostraba
 * `"CELLY" → Comida` sin decir de dónde salía "CELLY". Muestra la descripción
 * COMPLETA tal como Fino la compara, marca qué palabras usa la regla, deja
 * tocar palabras para hacerla más (o menos) precisa, y cuenta en vivo cuántos
 * movimientos anteriores coincidirían con ese texto.
 */
export function RuleScopeSheet(props: RuleScopeSheetProps) {
  return (
    <Modal visible transparent animationType="slide" onRequestClose={props.onClose}>
      <RuleScopeContent {...props} />
    </Modal>
  );
}

function RuleScopeContent({ transactionId, description, category, preview, busy, onChoose, onClose }: RuleScopeSheetProps) {
  const insets = useSafeAreaInsets();
  const previewMutation = useRulePreview();

  const normalized = preview.normalizedDescription || normalizeForDisplay(description);
  const words = useMemo(() => splitWords(normalized), [normalized]);
  const suggested = preview.suggestedPattern ?? (preview.isTooGeneric ? '' : preview.pattern);

  // Sugerencia demasiado genérica ("TRANSFERENCIA"): no se preselecciona nada;
  // la persona elige qué palabras identifican de verdad a estos movimientos.
  const [initialRange] = useState<WordRange | null>(() =>
    preview.isTooGeneric ? null : rangeForPattern(words, preview.pattern),
  );
  // Si la sugerencia no calza con palabras completas (caso raro), se sigue
  // usando tal cual mientras la persona no toque ninguna palabra.
  const [touched, setTouched] = useState(false);
  const [range, setRange] = useState<WordRange | null>(initialRange);
  const pattern = range
    ? patternFromRange(words, range)
    : !touched && !initialRange && !preview.isTooGeneric
      ? preview.pattern
      : '';

  // Última respuesta del backend (o error) para un texto concreto. "Revisando"
  // se deriva de que el texto actual todavía no tenga respuesta.
  const [result, setResult] = useState<{ pattern: string; preview: RulePreview | null; error: string | null }>({
    pattern: preview.pattern,
    preview,
    error: null,
  });
  const requestId = useRef(0);

  useEffect(() => {
    if (pattern.length === 0 || pattern === result.pattern) {
      return undefined;
    }

    const id = ++requestId.current;
    const timer = setTimeout(() => {
      previewMutation
        .mutateAsync({ transactionId, categoryId: category.id, pattern })
        .then((next) => {
          if (id === requestId.current) {
            setResult({ pattern, preview: next, error: null });
          }
        })
        .catch(() => {
          if (id === requestId.current) {
            setResult({ pattern, preview: null, error: 'No pudimos revisar ese texto. Inténtalo de nuevo.' });
          }
        });
    }, PREVIEW_DELAY_MS);

    return () => clearTimeout(timer);
    // previewMutation cambia de identidad en cada render; solo importa el texto.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pattern, result.pattern, transactionId, category.id]);

  const loading = pattern.length > 0 && result.pattern !== pattern;
  const current = !loading ? result.preview : null;
  const error = !loading ? result.error : null;
  const tooGeneric = current !== null && pattern.length > 0 && current.isTooGeneric;
  const canCreateRule = current !== null && pattern.length > 0 && !current.isTooGeneric;
  const matched = canCreateRule && current ? current.matchedCount : 0;
  const isSuggestion = pattern === suggested;

  const tap = (index: number) => {
    setTouched(true);
    setRange((value) => toggleWord(value, index));
  };

  const choose = (createRule: boolean, applyToExistingMatches: boolean) =>
    onChoose({
      createRule,
      applyToExistingMatches,
      rulePattern: createRule && !isSuggestion ? pattern : null,
    });

  const status = loading
    ? null
    : error
      ? { text: error, danger: true }
      : tooGeneric
        ? { text: 'Muy general. Agrega otra palabra.', danger: true }
        : canCreateRule && current
          ? {
              text:
                (matched === 0 ? 'Sin movimientos anteriores iguales.' : `${matched} anterior${matched === 1 ? '' : 'es'} coincide${matched === 1 ? '' : 'n'}.`) +
                (current.conflictingCategoryName ? ` Reemplaza tu regla de ${current.conflictingCategoryName}.` : ''),
              danger: false,
            }
          : pattern.length === 0
            ? { text: 'Elige al menos una palabra.', danger: false }
            : null;

  return (
    <View style={styles.root}>
      <Pressable style={styles.backdrop} onPress={onClose} accessibilityRole="button" accessibilityLabel="Cerrar" />

      <View style={[styles.sheet, { paddingBottom: insets.bottom + spacing.lg }]}>
        <View style={styles.handle} />
        <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={styles.content}>
          <Typo variant="heading">¿Usar {category.name} en otros parecidos?</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            Toca las palabras que identifican este movimiento.
          </Typo>

          <View style={styles.words} accessibilityLabel={`Descripción: ${normalized}`}>
            {words.map((word, index) => {
              const selected = range !== null && index >= range.start && index <= range.end;
              return (
                <Pressable
                  key={`${word}-${index}`}
                  onPress={() => tap(index)}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  accessibilityLabel={`${word}, ${selected ? 'incluida en la regla' : 'no incluida'}`}
                  hitSlop={4}
                  style={[styles.word, selected && styles.wordSelected]}
                >
                  <Typo variant={selected ? 'bodyStrong' : 'body'} color={selected ? colors.onAccent : colors.textSecondary}>
                    {word}
                  </Typo>
                </Pressable>
              );
            })}
          </View>

          <View style={styles.status}>
            {loading ? <ActivityIndicator size="small" color={colors.textSecondary} /> : null}
            {status ? (
              <Typo variant="caption" color={status.danger ? colors.danger : colors.textSecondary}>
                {status.text}
              </Typo>
            ) : null}
          </View>

          <View style={styles.actions}>
            <Button
              label="Este y los futuros"
              onPress={() => choose(true, false)}
              disabled={busy || !canCreateRule}
              softDisabled
            />
            {matched > 0 ? (
              <Button
                label={`También los anteriores (${matched})`}
                variant="secondary"
                onPress={() => choose(true, true)}
                disabled={busy || !canCreateRule}
              />
            ) : null}
            <Button label="Solo este movimiento" variant="ghost" onPress={() => choose(false, false)} disabled={busy} />
          </View>
        </ScrollView>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    justifyContent: 'flex-end',
  },
  backdrop: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    backgroundColor: colors.overlay,
  },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingTop: spacing.sm,
    paddingHorizontal: spacing.lg,
    maxHeight: '92%',
  },
  handle: {
    alignSelf: 'center',
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: colors.borderStrong,
    marginBottom: spacing.md,
  },
  content: {
    gap: spacing.md,
    paddingBottom: spacing.sm,
  },
  words: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  word: {
    paddingHorizontal: spacing.sm + 2,
    paddingVertical: spacing.xs + 2,
    borderRadius: radius.sm,
    borderWidth: 1,
    borderColor: colors.border,
  },
  wordSelected: {
    backgroundColor: colors.accent,
    borderColor: colors.accent,
  },
  status: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: 20,
  },
  actions: {
    gap: spacing.sm,
    marginTop: spacing.sm,
  },
});
