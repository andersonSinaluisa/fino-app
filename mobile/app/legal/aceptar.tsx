import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { colors, spacing } from '../../theme';
import { Button, Screen, SkeletonCard, Typo } from '../../components/ui';
import { ConsentCheckbox } from '../../components/legal/ConsentCheckbox';
import { useAcceptLegal, useLegalStatus } from '../../hooks/queries';
import { useAuthStore } from '../../store/authStore';
import { ApiError } from '../../services/apiClient';

/**
 * Se abre (desde el layout de pestañas) cuando la persona no aceptó la
 * versión vigente de los Términos o la Política de privacidad: cuentas
 * anteriores a este flujo o un cambio de versión. No se puede saltar: o
 * acepta, o cierra sesión (y puede eliminar su cuenta después).
 */
export default function AcceptLegalScreen() {
  const router = useRouter();
  const logout = useAuthStore((state) => state.logout);
  const { data: status } = useLegalStatus();
  const accept = useAcceptLegal();

  const [accepted, setAccepted] = useState(false);
  const [analytics, setAnalytics] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!status) {
    return (
      <Screen>
        <SkeletonCard />
      </Screen>
    );
  }

  // Si ya decidió sobre los datos de uso, no se vuelve a preguntar aquí.
  const askAnalytics = status.analyticsConsent === null;
  const firstTime = status.acceptedTermsVersion === null && status.acceptedPrivacyVersion === null;

  const submit = () => {
    setError(null);
    accept.mutate(
      {
        termsVersion: status.termsVersion,
        privacyVersion: status.privacyVersion,
        confirmedAdult: accepted,
        analyticsConsent: askAnalytics ? analytics : null,
      },
      {
        onSuccess: () => router.replace('/(tabs)'),
        onError: (caught) =>
          setError(caught instanceof ApiError ? caught.message : 'No pudimos guardar tu aceptación. Inténtalo de nuevo.'),
      },
    );
  };

  return (
    <Screen>
      <View style={styles.header}>
        <Typo variant="title">{firstTime ? 'Antes de seguir' : 'Actualizamos nuestros términos'}</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          {firstTime
            ? 'Para seguir usando Fino necesitamos que revises y aceptes cómo funciona y cómo cuidamos tus datos.'
            : 'Revisa los cambios y acéptalos para seguir usando Fino.'}
        </Typo>
      </View>

      <View style={styles.links}>
        <Button label="Leer Términos y condiciones" variant="secondary" onPress={() => router.push('/legal/terminos')} />
        <Button label="Leer Política de privacidad" variant="secondary" onPress={() => router.push('/legal/privacidad')} />
      </View>

      <View style={styles.form}>
        <ConsentCheckbox
          checked={accepted}
          onChange={setAccepted}
          accessibilityLabel="Tengo 18 años o más y acepto los Términos y condiciones y la Política de privacidad"
        >
          Tengo 18 años o más y acepto los Términos y condiciones y la Política de privacidad.
        </ConsentCheckbox>

        {askAnalytics ? (
          <ConsentCheckbox
            checked={analytics}
            onChange={setAnalytics}
            accessibilityLabel="Compartir datos de uso anónimos para mejorar Fino (opcional)"
          >
            Compartir datos de uso anónimos para mejorar Fino. Nunca incluyen montos ni movimientos. Opcional.
          </ConsentCheckbox>
        ) : null}

        {error ? (
          <Typo variant="caption" color={colors.danger}>
            {error}
          </Typo>
        ) : null}

        <Button label="Aceptar y continuar" onPress={submit} disabled={!accepted} softDisabled loading={accept.isPending} />
        <Button
          label="Cerrar sesión"
          variant="ghost"
          onPress={() => {
            void logout().then(() => router.replace('/(auth)/login'));
          }}
        />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: {
    marginTop: spacing.xxl,
    marginBottom: spacing.xl,
    gap: spacing.sm,
  },
  links: {
    gap: spacing.sm,
    marginBottom: spacing.xl,
  },
  form: {
    gap: spacing.lg,
  },
});
