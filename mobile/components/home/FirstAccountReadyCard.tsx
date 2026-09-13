import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Card, Typo } from '../ui';
import type { Account } from '../../types/api';

interface FirstAccountReadyCardProps {
  account: Account;
  accountCount: number;
  onAddAnother: () => void;
  onDismiss: () => void;
}

/**
 * Onboarding funcional: la tarjeta discreta post-onboarding del spec --
 * "Tu primera cuenta está lista ✓", se descarta y no vuelve a aparecer
 * (ver store/onboardingStore.ts). Vive en Home, no en el flujo mismo, porque
 * el flujo ya terminó -- esto es lo primero que ve la persona al llegar.
 */
export function FirstAccountReadyCard({ account, accountCount, onAddAnother, onDismiss }: FirstAccountReadyCardProps) {
  return (
    <Card style={styles.card}>
      <View style={styles.header}>
        <View style={styles.flex}>
          <Typo variant="bodyStrong">Tu primera cuenta está lista ✓</Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {account.providerName}
            {accountCount > 1 ? ` · ${accountCount} cuentas conectadas` : ''}
          </Typo>
        </View>
        <Pressable onPress={onDismiss} hitSlop={12} accessibilityRole="button" accessibilityLabel="Cerrar">
          <Ionicons name="close" size={18} color={colors.textSecondary} />
        </Pressable>
      </View>

      <Button label="Agregar otra cuenta" variant="secondary" compact fullWidth={false} onPress={onAddAnother} />
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: spacing.md,
    marginBottom: spacing.xl,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
    gap: 2,
  },
});
