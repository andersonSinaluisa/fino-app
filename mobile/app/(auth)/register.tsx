import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, Input, Screen, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';

const MIN_PASSWORD_LENGTH = 10;

export default function RegisterScreen() {
  const router = useRouter();
  const register = useAuthStore((state) => state.register);
  const error = useAuthStore((state) => state.error);
  const clearError = useAuthStore((state) => state.clearError);

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  const passwordTooShort = password.length > 0 && password.length < MIN_PASSWORD_LENGTH;
  const canSubmit = displayName.length > 1 && email.includes('@') && password.length >= MIN_PASSWORD_LENGTH;

  const submit = async () => {
    setBusy(true);
    try {
      await register(email, password, displayName);
      router.replace('/(tabs)');
    } catch {
      // Message is in the store.
    } finally {
      setBusy(false);
    }
  };

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <Screen>
        <View style={styles.header}>
          <Typo variant="title">Crea tu cuenta</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Nexo no mueve tu dinero: solo te ayuda a entenderlo.
          </Typo>
        </View>

        <View style={styles.form}>
          <Input
            label="Nombre"
            value={displayName}
            onChangeText={(value) => {
              clearError();
              setDisplayName(value);
            }}
            placeholder="Anderson"
            autoCapitalize="words"
          />

          <Input
            label="Correo"
            value={email}
            onChangeText={(value) => {
              clearError();
              setEmail(value);
            }}
            autoCapitalize="none"
            keyboardType="email-address"
            placeholder="tu@correo.com"
          />

          <Input
            label="Contraseña"
            value={password}
            onChangeText={(value) => {
              clearError();
              setPassword(value);
            }}
            secureTextEntry
            autoCapitalize="none"
            placeholder="Mínimo 10 caracteres"
            error={passwordTooShort ? `Usa al menos ${MIN_PASSWORD_LENGTH} caracteres.` : null}
          />

          {error ? (
            <Typo variant="caption" color={colors.danger}>
              {error}
            </Typo>
          ) : null}

          <Button label="Crear cuenta" onPress={() => void submit()} loading={busy} disabled={!canSubmit} />

          <Button label="Ya tengo cuenta" variant="ghost" onPress={() => router.back()} />
        </View>
      </Screen>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  header: {
    marginTop: spacing.xxl,
    marginBottom: spacing.xl,
    gap: spacing.xs,
  },
  form: {
    gap: spacing.lg,
  },
});
