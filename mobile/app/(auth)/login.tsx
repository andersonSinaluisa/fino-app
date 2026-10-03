import { useRef, useState } from 'react';
import { Alert, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { Link, useRouter } from 'expo-router';
import { colors, radius, spacing } from '../../theme';
import { Button, Input, Screen, Typo } from '../../components/ui';
import { AuthFooter, BrandHeader, PasswordInput, PrivacyHint } from '../../components/auth';
import { useAuthStore } from '../../store/authStore';

// Validación deliberadamente simple -- el backend es la fuente de verdad
// sobre si una cuenta existe; esto solo evita mandar un correo obviamente
// mal escrito y explica por qué "Entrar" no reacciona.
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function LoginScreen() {
  const router = useRouter();
  const login = useAuthStore((state) => state.login);
  const serverError = useAuthStore((state) => state.error);
  const clearError = useAuthStore((state) => state.clearError);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  // Solo se muestran los errores de validación después de un intento --
  // nadie quiere ver "correo inválido" mientras todavía está escribiendo.
  const [attempted, setAttempted] = useState(false);

  const passwordRef = useRef<TextInput>(null);

  const trimmedEmail = email.trim();
  const fieldsFilled = trimmedEmail.length > 0 && password.length > 0;
  const emailError = attempted && trimmedEmail.length > 0 && !EMAIL_PATTERN.test(trimmedEmail)
    ? 'Ingresa un correo válido.'
    : null;
  const passwordError = attempted && password.length === 0 ? 'La contraseña es obligatoria.' : null;

  const submit = async () => {
    setAttempted(true);

    if (!EMAIL_PATTERN.test(trimmedEmail) || password.length === 0) {
      return;
    }

    setBusy(true);
    try {
      const deletionCancelled = await login(trimmedEmail, password);
      if (deletionCancelled) {
        // Entregable 22: this is the one door back after "eliminar mi cuenta"
        // -- worth telling the person plainly what just happened.
        Alert.alert(
          'Tu cuenta sigue activa',
          'Habías pedido eliminar tu cuenta Fino. Como volviste a iniciar sesión, cancelamos esa solicitud.',
        );
      }
      router.replace('/(tabs)');
    } catch {
      // The store already holds the message; the screen just stops spinning.
    } finally {
      setBusy(false);
    }
  };

  return (
    <View style={styles.flex}>
      <Screen dismissKeyboardOnTap>
        <BrandHeader />

        <View style={styles.form}>
          <Input
            label="Correo"
            value={email}
            onChangeText={(value) => {
              clearError();
              setEmail(value);
            }}
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="email"
            keyboardType="email-address"
            placeholder="tu@correo.com"
            textContentType="emailAddress"
            returnKeyType="next"
            blurOnSubmit={false}
            onSubmitEditing={() => passwordRef.current?.focus()}
            error={emailError}
            style={styles.input}
            accessibilityLabel="Correo electrónico"
          />

          <View style={styles.passwordGroup}>
            <PasswordInput
              ref={passwordRef}
              label="Contraseña"
              value={password}
              onChangeText={(value) => {
                clearError();
                setPassword(value);
              }}
              placeholder="••••••••••"
              textContentType="password"
              returnKeyType="done"
              onSubmitEditing={() => void submit()}
              error={passwordError}
              accessibilityLabel="Contraseña"
            />

            <Link href="/(auth)/recuperar-password" asChild>
              <Pressable
                accessibilityRole="link"
                accessibilityLabel="Recuperar contraseña"
                hitSlop={8}
                style={styles.forgot}
              >
                <Typo variant="caption" color={colors.textSecondary}>
                  ¿La olvidaste?
                </Typo>
              </Pressable>
            </Link>
          </View>

          {serverError ? (
            <View style={styles.serverError}>
              <Typo variant="caption" color={colors.danger}>
                {serverError}
              </Typo>
            </View>
          ) : null}

          <Button
            label="Entrar"
            loadingLabel="Entrando..."
            onPress={() => void submit()}
            loading={busy}
            disabled={!fieldsFilled}
            softDisabled
            testID="login-submit"
            accessibilityLabel="Entrar"
          />
        </View>

        <AuthFooter
          question="¿Nuevo en Fino?"
          actionLabel="Crear cuenta"
          onPress={() => router.push('/(auth)/register')}
        />

        <PrivacyHint />
      </Screen>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  form: {
    gap: spacing.lg,
  },
  input: {
    minHeight: 60,
    borderRadius: radius.lg,
  },
  passwordGroup: {
    gap: spacing.sm,
  },
  forgot: {
    alignSelf: 'flex-end',
    minHeight: 32,
    justifyContent: 'center',
  },
  serverError: {
    backgroundColor: 'rgba(216, 102, 91, 0.08)',
    borderRadius: radius.md,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.lg,
  },
});
