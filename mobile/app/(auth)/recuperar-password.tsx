import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../theme';
import { Button, Screen, Typo } from '../../components/ui';

/**
 * Punto 5 del rediseño de Login: "¿La olvidaste?" necesita algo a dónde
 * navegar. El backend todavía no expone un endpoint de recuperación
 * (AuthEndpoints solo tiene register/login/refresh/logout/logout-all/
 * sessions/activity), así que esta pantalla no inventa una llamada al
 * servidor -- deja lista la ruta y la estructura visual para cuando exista.
 */
export default function RecoverPasswordScreen() {
  const router = useRouter();

  return (
    <Screen>
      <View style={styles.header}>
        <Ionicons name="lock-closed-outline" size={28} color={colors.text} />
        <Typo variant="title">Recuperar contraseña</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Todavía estamos construyendo este flujo. Muy pronto vas a poder restablecer tu
          contraseña directamente desde aquí.
        </Typo>
      </View>

      <Button
        label="Volver a iniciar sesión"
        onPress={() => router.back()}
        accessibilityLabel="Volver a iniciar sesión"
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    marginTop: spacing.xxl,
    marginBottom: spacing.xxl,
    gap: spacing.md,
  },
});
