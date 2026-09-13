import { Redirect, Stack } from 'expo-router';
import { colors } from '../../theme';
import { useAuthStore } from '../../store/authStore';

/**
 * Onboarding funcional: el flujo post-login (Bienvenida → Cómo funciona →
 * Elegir banco → BankImportFlow compartido en app/cuentas/). Vive fuera de
 * (auth) a propósito -- (auth) redirige a cualquier usuario autenticado
 * directo a (tabs), y este flujo necesita exactamente lo contrario.
 */
export default function OnboardingLayout() {
  const status = useAuthStore((state) => state.status);

  if (status === 'anonymous') {
    return <Redirect href="/(auth)/login" />;
  }

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: colors.background },
      }}
    />
  );
}
