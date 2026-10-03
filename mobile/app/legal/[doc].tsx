import { Pressable, StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, EmptyState, Screen, SkeletonCard, Typo } from '../../components/ui';
import { LegalMarkdown } from '../../components/legal/LegalMarkdown';
import { useLegalDocument } from '../../hooks/queries';

/** Términos y condiciones o Política de privacidad vigentes (/legal/terminos, /legal/privacidad). */
export default function LegalDocumentScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ doc: string }>();
  const kind = params.doc === 'terminos' ? 'terminos' : 'privacidad';
  const { data, isLoading, isError, refetch } = useLegalDocument(kind);

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back} accessibilityRole="button" accessibilityLabel="Volver">
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      {isError ? (
        <View style={styles.error}>
          <EmptyState icon="cloud-offline-outline" title="No pudimos cargar el documento" body="Revisa tu conexión e inténtalo de nuevo." />
          <Button label="Reintentar" variant="secondary" onPress={() => void refetch()} />
        </View>
      ) : isLoading || !data ? (
        <SkeletonCard />
      ) : (
        <LegalMarkdown markdown={data.markdown} />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  error: {
    gap: spacing.lg,
  },
});
