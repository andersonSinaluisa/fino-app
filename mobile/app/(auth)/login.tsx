import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, View } from 'react-native';
import { Link, useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, Input, Screen, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';

export default function LoginScreen() {
  const router = useRouter();
  const login = useAuthStore((state) => state.login);
  const error = useAuthStore((state) => state.error);
  const clearError = useAuthStore((state) => state.clearError);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    setBusy(true);
    try {
      await login(email, password);
      router.replace('/(tabs)');
    } catch {
      // The store already holds the message; the screen just stops spinning.
    } finally {
      setBusy(false);
    }
  };

  return (
    <KeyboardAvoidingView
      style={styles.flex}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <Screen>
        <View style={styles.header}>
          <Typo variant="title">Nexo</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Todo tu dinero, en un solo lugar.
          </Typo>
        </View>

        <View style={styles.form}>
          <Input
            label="Correo"
            value={email}
            onChangeText={(value) => {
              clearError();
              setEmail(value);
            }}
            autoCapitalize="none"
            autoComplete="email"
            keyboardType="email-address"
            placeholder="tu@correo.com"
            textContentType="emailAddress"
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
            placeholder="••••••••••"
            textContentType="password"
          />

          {error ? (
            <Typo variant="caption" color={colors.danger}>
              {error}
            </Typo>
          ) : null}

          <Button
            label="Entrar"
            onPress={() => void submit()}
            loading={busy}
            disabled={email.length === 0 || password.length === 0}
            testID="login-submit"
          />

          <View style={styles.footer}>
            <Typo variant="caption" color={colors.textSecondary}>
              ¿Todavía no tienes cuenta?{' '}
            </Typo>
            <Link href="/(auth)/register">
              <Typo variant="caption" color={colors.text}>
                Crear una
              </Typo>
            </Link>
          </View>
        </View>
      </Screen>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  header: {
    marginTop: spacing.xxxl,
    marginBottom: spacing.xxl,
    gap: spacing.xs,
  },
  form: {
    gap: spacing.lg,
  },
  footer: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: spacing.sm,
  },
});
