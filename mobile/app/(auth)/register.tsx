import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, Input, Screen, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { ConsentCheckbox } from '../../components/legal/ConsentCheckbox';

const MIN_PASSWORD_LENGTH = 10;

export default function RegisterScreen() {
  const router = useRouter();
  const { intent } = useLocalSearchParams<{ intent?: string }>();
  const register = useAuthStore((state) => state.register);
  const error = useAuthStore((state) => state.error);
  const clearError = useAuthStore((state) => state.clearError);

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  // LOPDP art. 8: ninguna casilla viene marcada. La primera es obligatoria
  // (18+ y aceptar los documentos); la de datos de uso es opcional.
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  const [analyticsConsent, setAnalyticsConsent] = useState(false);

  const passwordTooShort = password.length > 0 && password.length < MIN_PASSWORD_LENGTH;
  const canSubmit =
    displayName.length > 1 && email.includes('@') && password.length >= MIN_PASSWORD_LENGTH && acceptedTerms;

  const submit = async () => {
    setBusy(true);
    try {
      await register(email, password, displayName, {
        acceptedTerms,
        confirmedAdult: acceptedTerms,
        analyticsConsent,
      });
      // Coming from onboarding's "¿Qué quieres conectar?" step means the
      // person already intends to connect an account -- send them straight
      // into the real add-account flow instead of an empty home screen.
      router.replace(intent === 'connect' ? '/cuentas/agregar' : '/(tabs)');
    } catch {
      // Message is in the store.
    } finally {
      setBusy(false);
    }
  };

  return (
    <View style={styles.flex}>
      <Screen>
        <View style={styles.header}>
          <Typo variant="title">Crea tu cuenta</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Fino no mueve tu dinero: solo te ayuda a entenderlo.
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

          <ConsentCheckbox
            checked={acceptedTerms}
            onChange={setAcceptedTerms}
            accessibilityLabel="Tengo 18 años o más y acepto los Términos y condiciones y la Política de privacidad"
          >
            Tengo 18 años o más y acepto los{' '}
            <Typo variant="caption" style={styles.link} onPress={() => router.push('/legal/terminos')}>
              Términos y condiciones
            </Typo>{' '}
            y la{' '}
            <Typo variant="caption" style={styles.link} onPress={() => router.push('/legal/privacidad')}>
              Política de privacidad
            </Typo>
            .
          </ConsentCheckbox>

          <ConsentCheckbox
            checked={analyticsConsent}
            onChange={setAnalyticsConsent}
            accessibilityLabel="Compartir datos de uso anónimos para mejorar Fino (opcional)"
          >
            Compartir datos de uso anónimos para mejorar Fino. Nunca incluyen montos ni movimientos. Opcional.
          </ConsentCheckbox>

          {error ? (
            <Typo variant="caption" color={colors.danger}>
              {error}
            </Typo>
          ) : null}

          <Button label="Crear cuenta" onPress={() => void submit()} loading={busy} disabled={!canSubmit} />

          <Button label="Ya tengo cuenta" variant="ghost" onPress={() => router.back()} />
        </View>
      </Screen>
    </View>
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
  link: {
    textDecorationLine: 'underline',
    fontWeight: '700',
  },
});
