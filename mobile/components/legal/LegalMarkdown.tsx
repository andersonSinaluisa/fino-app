import { Fragment } from 'react';
import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Typo } from '../ui';

/** **negrita** dentro de una línea. */
function Inline({ text }: { text: string }) {
  const parts = text.split(/(\*\*[^*]+\*\*)/g).filter((part) => part.length > 0);
  return (
    <>
      {parts.map((part, index) =>
        part.startsWith('**') && part.endsWith('**') ? (
          <Typo key={index} variant="bodyStrong">
            {part.slice(2, -2)}
          </Typo>
        ) : (
          <Fragment key={index}>{part}</Fragment>
        ),
      )}
    </>
  );
}

/**
 * El markdown mínimo de los documentos legales (#, ##, -, **). El mismo texto
 * que el backend publica como página web: una sola fuente.
 */
export function LegalMarkdown({ markdown }: { markdown: string }) {
  const lines = markdown.split('\n');

  return (
    <View style={styles.root}>
      {lines.map((raw, index) => {
        const line = raw.trimEnd();
        if (line.length === 0) {
          return null;
        }

        if (line.startsWith('# ')) {
          return (
            <Typo key={index} variant="title">
              {line.slice(2)}
            </Typo>
          );
        }

        if (line.startsWith('## ')) {
          return (
            <Typo key={index} variant="subheading" style={styles.heading}>
              {line.slice(3)}
            </Typo>
          );
        }

        if (line.startsWith('- ')) {
          return (
            <View key={index} style={styles.bullet}>
              <Typo variant="body" color={colors.textSecondary}>
                •
              </Typo>
              <Typo variant="body" style={styles.flex}>
                <Inline text={line.slice(2)} />
              </Typo>
            </View>
          );
        }

        return (
          <Typo key={index} variant="body">
            <Inline text={line} />
          </Typo>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    gap: spacing.sm,
  },
  heading: {
    marginTop: spacing.lg,
  },
  bullet: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingLeft: spacing.xs,
  },
  flex: {
    flex: 1,
  },
});
